using System;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Jellyfin.Plugin.Loom.Models;

namespace Jellyfin.Plugin.Loom
{
    public static class LoomInterface
    {
        private static void Register(JObject payload)
        {
            var plugin = Plugin.Instance;
            if (plugin?.ServiceProvider == null)
            {
                throw new InvalidOperationException("Loom plugin service provider is not initialized.");
            }
            
            var writeService = plugin.ServiceProvider.GetRequiredService<ILoomWriteService>();
            writeService.Register(payload);
        }

        public static void RegisterTransformation(JObject payload)
        {
            Register(payload);
        }

        public static void UpdateTransformation(JObject payload)
        {
            var plugin = Plugin.Instance;
            if (plugin?.ServiceProvider == null)
            {
                throw new InvalidOperationException("Loom plugin service provider is not initialized.");
            }
            
            var writeService = plugin.ServiceProvider.GetRequiredService<ILoomWriteService>();
            writeService.Update(payload);
        }

        public static void DeregisterTransformation(Guid id)
        {
            var plugin = Plugin.Instance;
            if (plugin?.ServiceProvider == null)
            {
                return;
            }
            
            var registrar = plugin.ServiceProvider.GetRequiredService<Interfaces.ILoomRegistrar>();
            var target = System.Linq.Enumerable.FirstOrDefault(registrar.List(), e => e.Key.TransformationName == id.ToString());
            if (target != null)
            {
                registrar.Deregister(target.Key);
            }
        }
    }
}
