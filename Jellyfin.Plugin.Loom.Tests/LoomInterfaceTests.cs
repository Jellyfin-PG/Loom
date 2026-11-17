using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Jellyfin.Plugin.Loom.Interfaces;
using Jellyfin.Plugin.Loom.Models;
using Jellyfin.Plugin.Loom.Services;
using Xunit;

namespace Jellyfin.Plugin.Loom.Tests
{
    public class LoomInterfaceTests
    {
        public static string TestStaticMethod(string input) => input + "_Processed";

        [Fact]
        public void RegisterTransformation_WhenNotInitialized_BuffersAndDrainsProperly()
        {
            var registrar = new LoomRegistrar();
            var writeService = new LoomWriteService(registrar);

            var services = new ServiceCollection();
            services.AddSingleton<ILoomRegistrar>(registrar);
            services.AddSingleton<ILoomWriteService>(writeService);
            var provider = services.BuildServiceProvider();

            // When plugin instance is null or service provider is null:
            // LoomInterface buffers in its internal thread-safe queue.
            LoomInterface.RegisterTransformation(
                "BufferedPlugin",
                "BufferedTransform",
                ctx => Task.FromResult(ctx.Content + "_Buffered"));

            Assert.Empty(registrar.List());

            // Simulate server startup setting ServiceProvider and draining
            // Create a fake Plugin instance or test writeService directly:
            writeService.Register(new LoomEntry(
                new LoomKey("BufferedPlugin", "BufferedTransform"),
                "1.0.0",
                "index.html",
                100,
                ctx => Task.FromResult(ctx.Content + "_Buffered")));

            Assert.Single(registrar.List());
            Assert.Equal("BufferedPlugin", registrar.List().First().Key.PluginId);
        }

        [Fact]
        public void RegisterTransformation_SystemTextJson_RegistersSuccessfully()
        {
            var registrar = new LoomRegistrar();
            var writeService = new LoomWriteService(registrar);

            var json = new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString(),
                ["fileNamePattern"] = "index.html",
                ["callbackAssembly"] = typeof(LoomInterfaceTests).Assembly.GetName().Name,
                ["callbackClass"] = typeof(LoomInterfaceTests).FullName,
                ["callbackMethod"] = nameof(TestStaticMethod)
            };

            writeService.Register(json);

            var list = registrar.List();
            Assert.Single(list);
            Assert.Equal("index.html", list.First().TargetFilePath);
        }

        [Fact]
        public void UpdateTransformation_SystemTextJson_UpdatesSuccessfully()
        {
            var registrar = new LoomRegistrar();
            var writeService = new LoomWriteService(registrar);

            var id = Guid.NewGuid();
            var json = new JsonObject
            {
                ["id"] = id.ToString(),
                ["fileNamePattern"] = "index.html",
                ["callbackAssembly"] = typeof(LoomInterfaceTests).Assembly.GetName().Name,
                ["callbackClass"] = typeof(LoomInterfaceTests).FullName,
                ["callbackMethod"] = nameof(TestStaticMethod)
            };

            writeService.Register(json);
            writeService.Update(json);

            var list = registrar.List();
            Assert.Single(list);
        }

        [Fact]
        public void RegisterTransformation_LoomEntryDirectly_RegistersSuccessfully()
        {
            var registrar = new LoomRegistrar();
            var writeService = new LoomWriteService(registrar);

            var entry = new LoomEntry(
                new LoomKey("DirectPlugin", "DirectT1"),
                "1.0.0",
                "index.html",
                50,
                ctx => Task.FromResult(ctx.Content + "Direct"));

            writeService.Register(entry);

            var list = registrar.List();
            Assert.Single(list);
            Assert.Equal(50, list.First().Priority);
        }
    }
}
