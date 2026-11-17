using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller;
using Jellyfin.Plugin.Loom.Interfaces;
using Jellyfin.Plugin.Loom.Models;

namespace Jellyfin.Plugin.Loom.Middleware
{
    public class LoomMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILoomRegistrar _registrar;
        private readonly IServerApplicationPaths _appPaths;
        private readonly ILogger<LoomMiddleware> _logger;
        private readonly StartupBarrier _startupBarrier;

        private readonly ConcurrentDictionary<string, RenderedFile> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, RenderedFile> _lastKnownGood = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _compilationLocks = new(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan DefaultRegexTimeout = TimeSpan.FromMilliseconds(100);

        public int CachedFilesCount => _cache.Count;

        public LoomMiddleware(
            RequestDelegate next,
            ILoomRegistrar registrar,
            IServerApplicationPaths appPaths,
            ILogger<LoomMiddleware> logger,
            StartupBarrier startupBarrier)
        {
            _next = next;
            _registrar = registrar;
            _appPaths = appPaths;
            _logger = logger;
            _startupBarrier = startupBarrier;

            if (registrar is Services.LoomRegistrar reg)
            {
                reg.OnRegistryChanged += InvalidateCache;
            }
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var requestPath = context.Request.Path.Value ?? string.Empty;
            var relativePath = GetRelativeWebPath(requestPath);

            if (string.IsNullOrEmpty(relativePath))
            {
                await _next(context);
                return;
            }

            var physicalPath = Path.Combine(_appPaths.WebPath, relativePath);
            if (!File.Exists(physicalPath))
            {
                await _next(context);
                return;
            }

            var matches = FindMatchingTransformations(relativePath, requestPath);
            if (matches.Count == 0)
            {
                await _next(context);
                return;
            }

            if (!_startupBarrier.IsComplete)
            {
                _logger.LogWarning("Request for '{Path}' received before startup barrier cleared. Serving original content.", requestPath);
                await _next(context);
                return;
            }

            await _startupBarrier.WaitForStartupAsync();

            try
            {
                var rendered = await GetOrBuildRenderedFileAsync(relativePath, physicalPath, matches).ConfigureAwait(false);

                if (context.Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch) &&
                    ifNoneMatch.ToString() == rendered.ETag)
                {
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    return;
                }

                var contentTypeProvider = new FileExtensionContentTypeProvider();
                if (!contentTypeProvider.TryGetContentType(relativePath, out var contentType))
                {
                    contentType = "application/octet-stream";
                }

                context.Response.ContentType = contentType;
                context.Response.Headers.ETag = rendered.ETag;
                context.Response.ContentLength = rendered.Content.Length;

                await context.Response.Body.WriteAsync(rendered.Content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error executing Loom middleware for '{Path}'. Falling back to original file.", requestPath);
                await _next(context);
            }
        }

        private async Task<RenderedFile> GetOrBuildRenderedFileAsync(string relativePath, string physicalPath, List<LoomEntry> matches)
        {
            if (_cache.TryGetValue(relativePath, out var cached))
            {
                return cached;
            }

            var fileLock = _compilationLocks.GetOrAdd(relativePath, _ => new SemaphoreSlim(1, 1));
            await fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_cache.TryGetValue(relativePath, out cached))
                {
                    return cached;
                }

                try
                {
                    var buildResult = await BuildRenderedFileAsync(relativePath, physicalPath, matches).ConfigureAwait(false);
                    _cache[relativePath] = buildResult;
                    if (matches.All(m => m.Status == LoomStatus.Healthy))
                    {
                        _lastKnownGood[relativePath] = buildResult;
                    }
                    return buildResult;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to compile Loom transformation chain for '{Path}'. Attempting fallback.", relativePath);
                    if (_lastKnownGood.TryGetValue(relativePath, out var lkg))
                    {
                        return lkg;
                    }
                    throw;
                }
            }
            finally
            {
                fileLock.Release();
            }
        }

        private List<LoomEntry> FindMatchingTransformations(string relativePath, string requestPath)
        {
            var list = _registrar.List();
            var matches = new List<LoomEntry>();

            foreach (var entry in list)
            {
                if (!string.IsNullOrEmpty(entry.FileNamePattern))
                {
                    try
                    {
                        var pattern = entry.FileNamePattern;
                        if (!pattern.StartsWith("^") && !pattern.EndsWith("$") && !pattern.Contains("*") && !pattern.Contains("/") && !pattern.Contains("\\"))
                        {
                            pattern = $"(^|/){Regex.Escape(pattern)}$";
                        }

                        if (Regex.IsMatch(relativePath, pattern, RegexOptions.IgnoreCase, DefaultRegexTimeout) ||
                            Regex.IsMatch(requestPath, pattern, RegexOptions.IgnoreCase, DefaultRegexTimeout))
                        {
                            matches.Add(entry);
                        }
                    }
                    catch (RegexMatchTimeoutException ex)
                    {
                        _logger.LogWarning(ex, "Regex evaluation timed out for pattern '{Pattern}' registered by '{PluginId}'",
                            entry.FileNamePattern, entry.Key.PluginId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Invalid regex pattern '{Pattern}' registered by plugin '{PluginId}'",
                            entry.FileNamePattern, entry.Key.PluginId);
                    }
                }
                else
                {
                    if (entry.TargetFilePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(entry);
                    }
                }
            }

            return matches;
        }

        private async Task<RenderedFile> BuildRenderedFileAsync(string relativePath, string physicalPath, List<LoomEntry> matches)
        {
            var originalContent = await File.ReadAllTextAsync(physicalPath);
            var currentContent = originalContent;

            var sortedMatches = matches.OrderBy(m => m.Priority).ThenBy(m => m.RegisteredAt).ToList();
            bool anyFailed = false;

            foreach (var entry in sortedMatches)
            {
                try
                {
                    var ctx = new LoomContext(relativePath, currentContent);
                    currentContent = await entry.Transform(ctx);

                    entry.LastAppliedAt = DateTimeOffset.UtcNow;
                    entry.Status = LoomStatus.Healthy;
                    entry.LastError = null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error applying Loom transformation '{Name}' from '{Plugin}' on '{Path}'",
                        entry.Key.TransformationName, entry.Key.PluginId, relativePath);

                    entry.LastFailedAt = DateTimeOffset.UtcNow;
                    entry.LastError = ex.ToString();
                    entry.Status = LoomStatus.Degraded;
                    anyFailed = true;
                }
            }

            if (anyFailed && _lastKnownGood.TryGetValue(relativePath, out var lkg))
            {
                _logger.LogWarning("Transformation failed for '{Path}'. Serving last known good rendered version.", relativePath);
                return lkg;
            }

            var etagBuilder = new StringBuilder();
            etagBuilder.Append(File.GetLastWriteTimeUtc(physicalPath).Ticks);
            etagBuilder.Append('_');
            etagBuilder.Append(originalContent.Length);

            foreach (var entry in sortedMatches)
            {
                etagBuilder.Append('_');
                etagBuilder.Append(entry.Key.PluginId);
                etagBuilder.Append('_');
                etagBuilder.Append(entry.Key.TransformationName);
                etagBuilder.Append('_');
                etagBuilder.Append(entry.PluginVersion);
                etagBuilder.Append('_');
                etagBuilder.Append(entry.RegisteredAt.Ticks);
                if (entry.LastFailedAt.HasValue)
                {
                    etagBuilder.Append('_');
                    etagBuilder.Append(entry.LastFailedAt.Value.Ticks);
                }
            }

            var etagHash = ComputeHash(etagBuilder.ToString());
            var strongEtag = $"\"{etagHash}\"";
            var contentBytes = Encoding.UTF8.GetBytes(currentContent);

            return new RenderedFile(contentBytes, strongEtag, DateTimeOffset.UtcNow);
        }

        private void InvalidateCache(string path)
        {
            _logger.LogInformation("Loom transformation registry changed. Clearing in-memory file transformation cache.");
            _cache.Clear();
        }

        private static string ComputeHash(string input)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private string? GetRelativeWebPath(string requestPath)
        {
            requestPath = requestPath.Replace('\\', '/');

            var webIndex = requestPath.IndexOf("/web", StringComparison.OrdinalIgnoreCase);
            if (webIndex >= 0)
            {
                var subPath = requestPath.Substring(webIndex + 4);
                if (string.IsNullOrEmpty(subPath) || subPath == "/")
                {
                    return "index.html";
                }
                if (subPath.StartsWith("/"))
                {
                    return subPath.Substring(1);
                }
                return subPath;
            }

            if (string.IsNullOrEmpty(requestPath) || requestPath == "/")
            {
                return null;
            }
            if (requestPath.StartsWith("/"))
            {
                return requestPath.Substring(1);
            }
            return requestPath;
        }
    }

    public sealed record RenderedFile(byte[] Content, string ETag, DateTimeOffset BuiltAt);
}
