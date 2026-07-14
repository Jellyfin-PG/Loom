using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Jellyfin.Plugin.Loom.Configuration;

namespace Jellyfin.Plugin.Loom
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static Plugin? Instance { get; private set; }
        
        public IServiceProvider? ServiceProvider { get; internal set; }

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public override string Name => "Loom";
        
        public override Guid Id => Guid.Parse("3f8c8d8b-5a6b-4c28-98e6-e575a7bc2cd4");

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "Loom",
                    EmbeddedResourcePath = "Jellyfin.Plugin.Loom.Configuration.configPage.html",
                    DisplayName = "Loom",
                    EnableInMainMenu = true,
                    MenuSection = "server"
                }
            };
        }
    }

    public class LoomPluginInitializer : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        public LoomPluginInitializer(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (Plugin.Instance != null)
            {
                Plugin.Instance.ServiceProvider = _serviceProvider;
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
