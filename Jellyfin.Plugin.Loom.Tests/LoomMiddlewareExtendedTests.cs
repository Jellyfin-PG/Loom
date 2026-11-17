using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller;
using Jellyfin.Plugin.Loom.Interfaces;
using Jellyfin.Plugin.Loom.Models;
using Jellyfin.Plugin.Loom.Middleware;
using Jellyfin.Plugin.Loom.Services;
using Xunit;

namespace Jellyfin.Plugin.Loom.Tests
{
    public class LoomMiddlewareExtendedTests : IDisposable
    {
        private readonly string _tempWebDir;
        private readonly string _tempIndexFile;

        public LoomMiddlewareExtendedTests()
        {
            _tempWebDir = Path.Combine(Path.GetTempPath(), "LoomExtTestWeb_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempWebDir);
            _tempIndexFile = Path.Combine(_tempWebDir, "index.html");
            File.WriteAllText(_tempIndexFile, "<html><head></head><body>BaseContent</body></html>");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempWebDir))
            {
                Directory.Delete(_tempWebDir, true);
            }
        }

        private class TestAppPaths : IServerApplicationPaths
        {
            public string WebPath { get; }
            public TestAppPaths(string webPath) => WebPath = webPath;
            public string ProgramDataPath => string.Empty;
            public string LogDirectoryPath => string.Empty;
            public string ConfigurationDirectoryPath => string.Empty;
            public string CachePath => string.Empty;
            public string TempDirectoryPath => string.Empty;
            public string InternalMetadataPath => string.Empty;
            public string VirtualInternalMetadataPath => string.Empty;
            public string PluginsRepositoryPath => string.Empty;
            public string PluginConfigurationsPath => string.Empty;
            public string RootFolderPath => string.Empty;
            public string DefaultUserViewsPath => string.Empty;
            public string PeoplePath => string.Empty;
            public string GenrePath => string.Empty;
            public string MusicGenrePath => string.Empty;
            public string StudioPath => string.Empty;
            public string YearPath => string.Empty;
            public string UserConfigurationDirectoryPath => string.Empty;
            public string DefaultInternalMetadataPath => string.Empty;
            public string ArtistsPath => string.Empty;
            public void MakeSanityCheckOrThrow() { }
            public void CreateAndCheckMarker(string a, string b, bool c) { }
            public string ProgramSystemPath => string.Empty;
            public string DataPath => string.Empty;
            public string ImageCachePath => string.Empty;
            public string PluginsPath => string.Empty;
            public string SystemConfigurationFilePath => string.Empty;
            public string TempDirectory => string.Empty;
            public string VirtualDataPath => string.Empty;
            public string TrickplayPath => string.Empty;
            public string BackupPath => string.Empty;
        }

        private class TestLifetime : IHostApplicationLifetime
        {
            public CancellationToken ApplicationStarted { get; set; }
            public CancellationToken ApplicationStopping => throw new NotImplementedException();
            public CancellationToken ApplicationStopped => throw new NotImplementedException();
            public void StopApplication() => throw new NotImplementedException();
        }

        private class TestLogger<T> : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        }

        [Fact]
        public async Task TransformFailure_FallsBackGracefully_AndMarksStatusDegraded()
        {
            var registrar = new LoomRegistrar();
            var appPaths = new TestAppPaths(_tempWebDir);
            var logger = new TestLogger<LoomMiddleware>();

            var startedSource = new CancellationTokenSource();
            var lifetime = new TestLifetime { ApplicationStarted = startedSource.Token };
            var barrier = new StartupBarrier(lifetime, new TestLogger<StartupBarrier>());
            startedSource.Cancel();

            var failingEntry = new LoomEntry(
                new LoomKey("FailPlugin", "FailingT1"),
                "1.0.0",
                "index.html",
                100,
                ctx => throw new InvalidOperationException("Simulated plugin crash"));

            registrar.Register(failingEntry);

            var middleware = new LoomMiddleware(
                ctx => Task.CompletedTask,
                registrar,
                appPaths,
                logger,
                barrier);

            var context = new DefaultHttpContext();
            context.Request.Path = "/web/index.html";
            context.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(context);

            Assert.Equal(LoomStatus.Degraded, failingEntry.Status);
            Assert.NotNull(failingEntry.LastError);

            context.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(context.Response.Body);
            var responseBody = await reader.ReadToEndAsync();
            Assert.Contains("BaseContent", responseBody);
        }

        [Fact]
        public async Task TransformFailure_WithPriorSuccess_UsesLastKnownGood()
        {
            var registrar = new LoomRegistrar();
            var appPaths = new TestAppPaths(_tempWebDir);
            var logger = new TestLogger<LoomMiddleware>();

            var startedSource = new CancellationTokenSource();
            var lifetime = new TestLifetime { ApplicationStarted = startedSource.Token };
            var barrier = new StartupBarrier(lifetime, new TestLogger<StartupBarrier>());
            startedSource.Cancel();

            bool shouldFail = false;
            var flappyEntry = new LoomEntry(
                new LoomKey("FlappyPlugin", "FlappyT1"),
                "1.0.0",
                "index.html",
                100,
                ctx =>
                {
                    if (shouldFail)
                    {
                        throw new InvalidOperationException("Failed on second run");
                    }
                    return Task.FromResult(ctx.Content.Replace("BaseContent", "FlappySuccess"));
                });

            registrar.Register(flappyEntry);

            var middleware = new LoomMiddleware(
                ctx => Task.CompletedTask,
                registrar,
                appPaths,
                logger,
                barrier);

            // First run - succeeds
            var context1 = new DefaultHttpContext();
            context1.Request.Path = "/web/index.html";
            context1.Response.Body = new MemoryStream();
            await middleware.InvokeAsync(context1);

            context1.Response.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(context1.Response.Body))
            {
                var body = await reader.ReadToEndAsync();
                Assert.Contains("FlappySuccess", body);
            }

            // Invalidate cache and simulate failure on rebuild
            registrar.InvalidateCache("index.html");
            shouldFail = true;

            var context2 = new DefaultHttpContext();
            context2.Request.Path = "/web/index.html";
            context2.Response.Body = new MemoryStream();
            await middleware.InvokeAsync(context2);

            // Should have served last known good
            context2.Response.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(context2.Response.Body))
            {
                var body = await reader.ReadToEndAsync();
                Assert.Contains("FlappySuccess", body);
            }
        }

        [Fact]
        public async Task ConcurrentRequests_ExecuteTransformOnce_CacheStampedeProtected()
        {
            var registrar = new LoomRegistrar();
            var appPaths = new TestAppPaths(_tempWebDir);
            var logger = new TestLogger<LoomMiddleware>();

            var startedSource = new CancellationTokenSource();
            var lifetime = new TestLifetime { ApplicationStarted = startedSource.Token };
            var barrier = new StartupBarrier(lifetime, new TestLogger<StartupBarrier>());
            startedSource.Cancel();

            int transformCount = 0;
            var countingEntry = new LoomEntry(
                new LoomKey("CountPlugin", "CountT1"),
                "1.0.0",
                "index.html",
                100,
                async ctx =>
                {
                    Interlocked.Increment(ref transformCount);
                    await Task.Delay(20);
                    return ctx.Content.Replace("BaseContent", "StampedeProtected");
                });

            registrar.Register(countingEntry);

            var middleware = new LoomMiddleware(
                ctx => Task.CompletedTask,
                registrar,
                appPaths,
                logger,
                barrier);

            var tasks = new Task[10];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    var ctx = new DefaultHttpContext();
                    ctx.Request.Path = "/web/index.html";
                    ctx.Response.Body = new MemoryStream();
                    await middleware.InvokeAsync(ctx);
                });
            }

            await Task.WhenAll(tasks);

            Assert.Equal(1, transformCount);
            Assert.Equal(1, middleware.CachedFilesCount);
        }
    }
}
