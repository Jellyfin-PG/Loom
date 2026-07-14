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
        
        private readonly ConcurrentDictionary<string, RenderedFile> _cache = new();
        private readonly ConcurrentDictionary<string, RenderedFile> _lastKnownGood = new();

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
                var rendered = await _cache.GetOrAddAsync(relativePath, async path =>
                {
                    try
                    {
                        var buildResult = await BuildRenderedFileAsync(relativePath, physicalPath, matches);
                        _lastKnownGood[relativePath] = buildResult;
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
                });

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

        private List<LoomEntry> FindMatchingTransformations(string relativePath, string requestPath)
        {
            var list = _registrar.List();
            var matches = new List<LoomEntry>();

            foreach (var entry in list)
            {
                if (entry.FileNamePattern != null)
                {
                    try
                    {
                        if (Regex.IsMatch(relativePath, entry.FileNamePattern, RegexOptions.IgnoreCase) ||
                            Regex.IsMatch(requestPath, entry.FileNamePattern, RegexOptions.IgnoreCase))
                        {
                            matches.Add(entry);
                        }
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
                }
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

            var etagHash = ComputeMd5(etagBuilder.ToString());
            var strongEtag = $"\"{etagHash}\"";
            var contentBytes = Encoding.UTF8.GetBytes(currentContent);

            return new RenderedFile(contentBytes, strongEtag, DateTimeOffset.UtcNow);
        }

        private void InvalidateCache(string path)
        {
            _logger.LogInformation("Loom transformation registry changed. Clearing in-memory file transformation cache.");
            _cache.Clear();
        }

        private string ComputeMd5(string input)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
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
                return "index.html";
            }
            if (requestPath.StartsWith("/"))
            {
                return requestPath.Substring(1);
            }
            return requestPath;
        }
    }

    public sealed record RenderedFile(byte[] Content, string ETag, DateTimeOffset BuiltAt);

    internal static class ConcurrentDictionaryExtensions
    {
        public static async Task<TValue> GetOrAddAsync<TKey, TValue>(
            this ConcurrentDictionary<TKey, TValue> dictionary,
            TKey key,
            Func<TKey, Task<TValue>> valueFactory) where TKey : notnull
        {
            if (dictionary.TryGetValue(key, out var value))
            {
                return value;
            }

            var newValue = await valueFactory(key);
            dictionary[key] = newValue;
            return newValue;
        }
    }
}
