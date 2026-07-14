using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Jellyfin.Plugin.Loom.Interfaces;
using Jellyfin.Plugin.Loom.Models;
using Jellyfin.Plugin.Loom.Services;
using Jellyfin.Plugin.Loom.Middleware;

namespace Jellyfin.Plugin.Loom
{
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<ILoomRegistrar, LoomRegistrar>();
            serviceCollection.AddSingleton<ILoomWriteService, LoomWriteService>();
            serviceCollection.AddTransient<IStartupFilter, LoomStartupFilter>();
            serviceCollection.AddSingleton<StartupBarrier>();
            
            serviceCollection.AddHostedService<LoomPluginInitializer>();
        }
    }
}
