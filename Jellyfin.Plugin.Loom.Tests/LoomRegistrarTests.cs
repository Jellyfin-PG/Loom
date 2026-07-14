using Xunit;
using Jellyfin.Plugin.Loom.Interfaces;
using Jellyfin.Plugin.Loom.Models;
using Jellyfin.Plugin.Loom.Services;
using System.Threading.Tasks;
using System.Linq;

namespace Jellyfin.Plugin.Loom.Tests
{
    public class LoomRegistrarTests
    {
        [Fact]
        public void Register_ShouldAddEntry_AndTriggerEvent()
        {
            var registrar = new LoomRegistrar();
            string? eventPath = null;
            registrar.OnRegistryChanged += path => eventPath = path;

            var key = new LoomKey("PluginA", "TransformA");
            var entry = new LoomEntry(key, "1.0.0", "index.html", 100, ctx => Task.FromResult(ctx.Content + "A"));

            registrar.Register(entry);

            var list = registrar.List();
            Assert.Single(list);
            Assert.Equal(entry, list.First());
            Assert.Equal("index.html", eventPath);
        }

        [Fact]
        public void Update_ShouldModifyExistingEntry_AndTriggerEvent()
        {
            var registrar = new LoomRegistrar();
            string? eventPath = null;
            registrar.OnRegistryChanged += path => eventPath = path;

            var key = new LoomKey("PluginA", "TransformA");
            var entry1 = new LoomEntry(key, "1.0.0", "index.html", 100, ctx => Task.FromResult(ctx.Content + "A"));
            var entry2 = new LoomEntry(key, "1.1.0", "index.html", 50, ctx => Task.FromResult(ctx.Content + "B"));

            registrar.Register(entry1);
            registrar.Update(entry2);

            var list = registrar.List();
            Assert.Single(list);
            Assert.Equal("1.1.0", list.First().PluginVersion);
            Assert.Equal(50, list.First().Priority);
            Assert.Equal("index.html", eventPath);
        }

        [Fact]
        public void Deregister_ShouldRemoveEntry_AndTriggerEvent()
        {
            var registrar = new LoomRegistrar();
            var key = new LoomKey("PluginA", "TransformA");
            var entry = new LoomEntry(key, "1.0.0", "index.html", 100, ctx => Task.FromResult(ctx.Content + "A"));
            registrar.Register(entry);

            string? eventPath = null;
            registrar.OnRegistryChanged += path => eventPath = path;

            var result = registrar.Deregister(key);

            Assert.True(result);
            Assert.Empty(registrar.List());
            Assert.Equal("index.html", eventPath);
        }

        [Fact]
        public void List_WithFilter_ShouldReturnOnlyMatchingEntries()
        {
            var registrar = new LoomRegistrar();
            var entry1 = new LoomEntry(new LoomKey("P1", "T1"), "1.0", "index.html", 100, ctx => Task.FromResult(ctx.Content));
            var entry2 = new LoomEntry(new LoomKey("P2", "T2"), "1.0", "main.js", 100, ctx => Task.FromResult(ctx.Content));
            
            registrar.Register(entry1);
            registrar.Register(entry2);

            var indexList = registrar.List("index.html");
            var mainList = registrar.List("main.js");

            Assert.Single(indexList);
            Assert.Equal(entry1.Key, indexList.First().Key);
            
            Assert.Single(mainList);
            Assert.Equal(entry2.Key, mainList.First().Key);
        }
    }
}
