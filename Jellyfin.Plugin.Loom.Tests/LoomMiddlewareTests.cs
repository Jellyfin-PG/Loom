using System;
using System.Collections.Generic;
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
    public class LoomMiddlewareTests : IDisposable
    {
        private readonly string _tempWebDir;
        private readonly string _tempIndexFile;

        public LoomMiddlewareTests()
        {
            _tempWebDir = Path.Combine(Path.GetTempPath(), "LoomTestWeb_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempWebDir);
            _tempIndexFile = Path.Combine(_tempWebDir, "index.html");
            File.WriteAllText(_tempIndexFile, "<html><head></head><body>Original</body></html>");
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
        public async Task InvokeAsync_NoMatches_ShouldPassThrough()
        {
            var registrar = new LoomRegistrar();
            var appPaths = new TestAppPaths(_tempWebDir);
            var logger = new TestLogger<LoomMiddleware>();

            var startedSource = new CancellationTokenSource();
            var lifetime = new TestLifetime { ApplicationStarted = startedSource.Token };
            var barrierLogger = new TestLogger<StartupBarrier>();
            var barrier = new StartupBarrier(lifetime, barrierLogger);
            startedSource.Cancel();

            bool nextCalled = false;
            RequestDelegate next = ctx =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new LoomMiddleware(next, registrar, appPaths, logger, barrier);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/web/index.html";

            await middleware.InvokeAsync(httpContext);

            Assert.True(nextCalled);
        }

        [Fact]
        public async Task InvokeAsync_WithMatch_ShouldTransformContent()
        {
            var registrar = new LoomRegistrar();
            var appPaths = new TestAppPaths(_tempWebDir);
            var logger = new TestLogger<LoomMiddleware>();

            var startedSource = new CancellationTokenSource();
            var lifetime = new TestLifetime { ApplicationStarted = startedSource.Token };
            var barrier = new StartupBarrier(lifetime, new TestLogger<StartupBarrier>());

            var key = new LoomKey("TestPlugin", "Rule1");
            var entry = new LoomEntry(key, "1.0", "index.html", 100, ctx => Task.FromResult(ctx.Content.Replace("Original", "Transformed")));
            registrar.Register(entry);

            startedSource.Cancel();

            bool nextCalled = false;
            RequestDelegate next = ctx =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new LoomMiddleware(next, registrar, appPaths, logger, barrier);

            var httpContext = new DefaultHttpContext();
            var responseBody = new MemoryStream();
            httpContext.Response.Body = responseBody;
            httpContext.Request.Path = "/web/index.html";

            await middleware.InvokeAsync(httpContext);

            Assert.False(nextCalled);
            Assert.Equal("text/html", httpContext.Response.ContentType);
            Assert.NotEmpty(httpContext.Response.Headers.ETag.ToString());

            var outputString = Encoding.UTF8.GetString(responseBody.ToArray());
            Assert.Contains("Transformed", outputString);
            Assert.DoesNotContain("Original", outputString);
        }

        [Fact]
        public async Task InvokeAsync_ThrowingTransformation_ShouldDegradeGracefully()
        {
            var registrar = new LoomRegistrar();
            var appPaths = new TestAppPaths(_tempWebDir);

            var startedSource = new CancellationTokenSource();
            var lifetime = new TestLifetime { ApplicationStarted = startedSource.Token };
            var barrier = new StartupBarrier(lifetime, new TestLogger<StartupBarrier>());

            var keyA = new LoomKey("PluginA", "RuleA");
            var entryA = new LoomEntry(keyA, "1.0", "index.html", 50, ctx => Task.FromResult(ctx.Content.Replace("Original", "A")));

            var keyB = new LoomKey("PluginB", "RuleB");
            var entryB = new LoomEntry(keyB, "1.0", "index.html", 100, ctx => throw new Exception("Simulation Error"));

            registrar.Register(entryA);
            registrar.Register(entryB);

            startedSource.Cancel();

            var middleware = new LoomMiddleware(ctx => Task.CompletedTask, registrar, appPaths, new TestLogger<LoomMiddleware>(), barrier);

            var httpContext = new DefaultHttpContext();
            var responseBody = new MemoryStream();
            httpContext.Response.Body = responseBody;
            httpContext.Request.Path = "/web/index.html";

            await middleware.InvokeAsync(httpContext);

            var outputString = Encoding.UTF8.GetString(responseBody.ToArray());
            Assert.Contains("A", outputString);
            Assert.Equal(LoomStatus.Degraded, entryB.Status);
            Assert.NotNull(entryB.LastError);
        }

        [Fact]
        public async Task InvokeAsync_EmptyFileNamePattern_ShouldNotMatchEverything()
        {
            var registrar = new LoomRegistrar();
            var appPaths = new TestAppPaths(_tempWebDir);
            var logger = new TestLogger<LoomMiddleware>();

            var startedSource = new CancellationTokenSource();
            var lifetime = new TestLifetime { ApplicationStarted = startedSource.Token };
            var barrier = new StartupBarrier(lifetime, new TestLogger<StartupBarrier>());

            var key = new LoomKey("TestPlugin", "RuleEmptyPattern");
            var entry = new LoomEntry(key, "1.0", "index.html", 100, ctx => Task.FromResult(ctx.Content.Replace("Original", "Transformed")))
            {
                FileNamePattern = ""
            };
            registrar.Register(entry);

            startedSource.Cancel();

            bool nextCalled = false;
            RequestDelegate next = ctx =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new LoomMiddleware(next, registrar, appPaths, logger, barrier);

            var mainJsPath = Path.Combine(_tempWebDir, "main.js");
            File.WriteAllText(mainJsPath, "console.log('original js');");

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/web/main.js";

            await middleware.InvokeAsync(httpContext);

            Assert.True(nextCalled);
        }

        [Fact]
        public async Task InvokeAsync_SubstringFileNamePattern_ShouldNotMatchPartially()
        {
            var registrar = new LoomRegistrar();
            var appPaths = new TestAppPaths(_tempWebDir);
            var logger = new TestLogger<LoomMiddleware>();

            var startedSource = new CancellationTokenSource();
            var lifetime = new TestLifetime { ApplicationStarted = startedSource.Token };
            var barrier = new StartupBarrier(lifetime, new TestLogger<StartupBarrier>());

            var key = new LoomKey("TestPlugin", "RuleIndexHtml");
            var entry = new LoomEntry(key, "1.0", "index.html", 100, ctx => Task.FromResult(ctx.Content.Replace("Original", "Transformed")))
            {
                FileNamePattern = "index.html"
            };
            registrar.Register(entry);

            startedSource.Cancel();

            bool nextCalled = false;
            RequestDelegate next = ctx =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new LoomMiddleware(next, registrar, appPaths, logger, barrier);

            var chunkJsPath = Path.Combine(_tempWebDir, "session-login-index-html.chunk.js");
            File.WriteAllText(chunkJsPath, "console.log('original chunk');");

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/web/session-login-index-html.chunk.js";

            await middleware.InvokeAsync(httpContext);

            Assert.True(nextCalled);
        }

        [Fact]
        public async Task InvokeAsync_CoexistenceWithUpstreamFileTransformation_ShouldWriteToBufferWithoutCallingNext()
        {
            var registrar = new LoomRegistrar();
            var appPaths = new TestAppPaths(_tempWebDir);
            var logger = new TestLogger<LoomMiddleware>();

            var startedSource = new CancellationTokenSource();
            var lifetime = new TestLifetime { ApplicationStarted = startedSource.Token };
            var barrier = new StartupBarrier(lifetime, new TestLogger<StartupBarrier>());

            var key = new LoomKey("TestPlugin", "Rule1");
            var entry = new LoomEntry(key, "1.0", "index.html", 100, ctx => Task.FromResult(ctx.Content.Replace("Original", "LoomModified")));
            registrar.Register(entry);

            startedSource.Cancel();

            // Next middleware in ASP.NET pipeline (e.g. StaticFileMiddleware) should NEVER be called
            // when Loom generates the response, to prevent overwriting the transformed buffer with disk contents.
            bool nextCalled = false;
            RequestDelegate next = ctx =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new LoomMiddleware(next, registrar, appPaths, logger, barrier);

            // Upstream FileTransformationMiddleware buffers the response body using a MemoryStream.
            var fakeFtService = new FakeFtReadService();
            var serviceProvider = new FakeServiceProvider(fakeFtService);

            var httpContext = new DefaultHttpContext();
            httpContext.RequestServices = serviceProvider;
            var responseBody = new MemoryStream();
            httpContext.Response.Body = responseBody;
            httpContext.Request.Path = "/web/index.html";

            await middleware.InvokeAsync(httpContext);

            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
            var finalOutput = Encoding.UTF8.GetString(responseBody.ToArray());
            Assert.Contains("LoomModified", finalOutput);
        }

        [Fact]
        public async Task InvokeAsync_CoexistenceWithDownstreamFileTransformation_ShouldApplyTransformations()
        {
            var registrar = new LoomRegistrar();
            var appPaths = new TestAppPaths(_tempWebDir);
            var logger = new TestLogger<LoomMiddleware>();

            var startedSource = new CancellationTokenSource();
            var lifetime = new TestLifetime { ApplicationStarted = startedSource.Token };
            var barrier = new StartupBarrier(lifetime, new TestLogger<StartupBarrier>());

            var key = new LoomKey("TestPlugin", "Rule1");
            var entry = new LoomEntry(key, "1.0", "index.html", 100, ctx => Task.FromResult(ctx.Content.Replace("Original", "LoomModified")));
            registrar.Register(entry);

            startedSource.Cancel();

            bool nextCalled = false;
            RequestDelegate next = ctx =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new LoomMiddleware(next, registrar, appPaths, logger, barrier);

            var fakeFtService = new FakeFtReadService();
            var serviceProvider = new FakeServiceProvider(fakeFtService);

            var httpContext = new DefaultHttpContext();
            httpContext.RequestServices = serviceProvider;
            // Use a non-seekable stream to simulate a downstream scenario where FileTransformation has not wrapped Body
            using var backingStream = new MemoryStream();
            var nonSeekableStream = new NonSeekableStream(backingStream);
            httpContext.Response.Body = nonSeekableStream;
            httpContext.Request.Path = "/web/index.html";

            await middleware.InvokeAsync(httpContext);

            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
            var finalOutput = Encoding.UTF8.GetString(backingStream.ToArray());
            Assert.Contains("LoomModified_And_DownstreamFtModified", finalOutput);
        }

        private class NonSeekableStream : Stream
        {
            private readonly Stream _inner;
            public NonSeekableStream(Stream inner) => _inner = inner;
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.WriteAsync(buffer, offset, count, cancellationToken);
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _inner.WriteAsync(buffer, cancellationToken);
        }

        private interface IWebFileTransformationReadService
        {
            bool NeedsTransformation(string path);
            Task RunTransformation(string path, Stream stream);
        }

        private class FakeFtReadService : IWebFileTransformationReadService
        {
            public bool NeedsTransformation(string path) => path.Contains("index.html");

            public async Task RunTransformation(string path, Stream stream)
            {
                stream.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, leaveOpen: true);
                var text = await reader.ReadToEndAsync();
                var transformed = text.Replace("LoomModified", "LoomModified_And_DownstreamFtModified");
                stream.SetLength(0);
                using var writer = new StreamWriter(stream, leaveOpen: true);
                await writer.WriteAsync(transformed);
                await writer.FlushAsync();
            }
        }

        private class FakeServiceProvider : IServiceProvider
        {
            private readonly object _service;
            public FakeServiceProvider(object service) => _service = service;

            public object? GetService(Type serviceType)
            {
                if (serviceType.Name.Contains("IWebFileTransformationReadService"))
                {
                    return _service;
                }
                return null;
            }
        }
    }
}
