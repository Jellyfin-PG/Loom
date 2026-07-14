using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Loom.Interfaces;
using Jellyfin.Plugin.Loom.Models;

namespace Jellyfin.Plugin.Loom.Services
{
    public class LoomRegistrar : ILoomRegistrar
    {
        private readonly ConcurrentDictionary<LoomKey, LoomEntry> _registry = new();
        
        public event Action<string>? OnRegistryChanged;

        public void Register(LoomEntry entry)
        {
            ApplyPriorityOverride(entry);
            _registry[entry.Key] = entry;
            OnRegistryChanged?.Invoke(entry.TargetFilePath);
        }

        public void Update(LoomEntry entry)
        {
            ApplyPriorityOverride(entry);
            _registry[entry.Key] = entry;
            OnRegistryChanged?.Invoke(entry.TargetFilePath);
        }

        public bool Deregister(LoomKey key)
        {
            if (_registry.TryRemove(key, out var entry))
            {
                OnRegistryChanged?.Invoke(entry.TargetFilePath);
                return true;
            }
            return false;
        }

        public IReadOnlyCollection<LoomEntry> List(string? targetFilePath = null)
        {
            if (string.IsNullOrEmpty(targetFilePath))
            {
                return _registry.Values.ToList();
            }
            
            var normalizedTarget = NormalizePath(targetFilePath);
            return _registry.Values
                .Where(e => NormalizePath(e.TargetFilePath) == normalizedTarget)
                .ToList();
        }

        public LoomEntry? GetStatus(LoomKey key)
        {
            return _registry.TryGetValue(key, out var entry) ? entry : null;
        }

        public void InvalidateCache(string targetFilePath)
        {
            OnRegistryChanged?.Invoke(targetFilePath);
        }

        private void ApplyPriorityOverride(LoomEntry entry)
        {
            var config = Plugin.Instance?.Configuration;
            if (config != null)
            {
                var over = config.PriorityOverrides.FirstOrDefault(o => 
                    o.PluginId.Equals(entry.Key.PluginId, StringComparison.OrdinalIgnoreCase) &&
                    o.TransformationName.Equals(entry.Key.TransformationName, StringComparison.OrdinalIgnoreCase));
                
                if (over != null)
                {
                    entry.Priority = over.Priority;
                }
            }
        }

        private string NormalizePath(string path)
        {
            return path.Replace('\\', '/').Trim('/');
        }
    }
}
