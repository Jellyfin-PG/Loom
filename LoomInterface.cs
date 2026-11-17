using System;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Jellyfin.Plugin.Loom.Interfaces;
using Jellyfin.Plugin.Loom.Models;

namespace Jellyfin.Plugin.Loom
{
    public static class LoomInterface
    {
        private static readonly ConcurrentQueue<Action<ILoomWriteService>> PendingActions = new();

        /// <summary>
        /// Drains any pending transformations that were registered before the service provider finished initializing.
        /// </summary>
        public static void DrainPendingRegistrations()
        {
            var plugin = Plugin.Instance;
            if (plugin?.ServiceProvider == null)
            {
                return;
            }

            var writeService = plugin.ServiceProvider.GetService<ILoomWriteService>();
            if (writeService == null)
            {
                return;
            }

            while (PendingActions.TryDequeue(out var action))
            {
                action(writeService);
            }
        }

        private static void ExecuteOrQueue(Action<ILoomWriteService> action)
        {
            var plugin = Plugin.Instance;
            if (plugin?.ServiceProvider != null)
            {
                DrainPendingRegistrations();
                var writeService = plugin.ServiceProvider.GetRequiredService<ILoomWriteService>();
                action(writeService);
            }
            else
            {
                PendingActions.Enqueue(action);
            }
        }

        public static void RegisterTransformation(JObject payload)
        {
            ExecuteOrQueue(ws => ws.Register(payload));
        }

        public static void UpdateTransformation(JObject payload)
        {
            ExecuteOrQueue(ws => ws.Update(payload));
        }

        public static void RegisterTransformation(JsonObject payload)
        {
            ExecuteOrQueue(ws => ws.Register(payload));
        }

        public static void UpdateTransformation(JsonObject payload)
        {
            ExecuteOrQueue(ws => ws.Update(payload));
        }

        public static void RegisterTransformation(LoomEntry entry)
        {
            ExecuteOrQueue(ws => ws.Register(entry));
        }

        public static void UpdateTransformation(LoomEntry entry)
        {
            ExecuteOrQueue(ws => ws.Update(entry));
        }

        public static void RegisterTransformation(
            string pluginId,
            string transformationName,
            Func<LoomContext, Task<string>> transform,
            string targetFilePath = "index.html",
            int priority = 100,
            string? fileNamePattern = null,
            string pluginVersion = "1.0.0")
        {
            var entry = new LoomEntry(
                new LoomKey(pluginId, transformationName),
                pluginVersion,
                targetFilePath,
                priority,
                transform)
            {
                FileNamePattern = fileNamePattern
            };

            RegisterTransformation(entry);
        }

        public static void DeregisterTransformation(Guid id)
        {
            var plugin = Plugin.Instance;
            if (plugin?.ServiceProvider == null)
            {
                return;
            }

            var registrar = plugin.ServiceProvider.GetRequiredService<ILoomRegistrar>();
            var target = System.Linq.Enumerable.FirstOrDefault(registrar.List(), e => e.Key.TransformationName == id.ToString());
            if (target != null)
            {
                registrar.Deregister(target.Key);
            }
        }

        public static void DeregisterTransformation(string pluginId, string transformationName)
        {
            var plugin = Plugin.Instance;
            if (plugin?.ServiceProvider == null)
            {
                return;
            }

            var registrar = plugin.ServiceProvider.GetRequiredService<ILoomRegistrar>();
            registrar.Deregister(new LoomKey(pluginId, transformationName));
        }
    }
}
