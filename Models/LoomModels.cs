using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Loom.Models
{
    public interface ILoomWriteService
    {
        void Register(Newtonsoft.Json.Linq.JObject payload);
        void Update(Newtonsoft.Json.Linq.JObject payload);
    }

    public sealed record LoomKey(string PluginId, string TransformationName);

    public sealed record LoomContext(string TargetFilePath, string Content);

    public enum LoomStatus
    {
        Healthy,
        Degraded,
        Failed
    }

    public sealed class LoomEntry
    {
        public LoomKey Key { get; }
        public string PluginVersion { get; }
        public string TargetFilePath { get; }
        public string? FileNamePattern { get; set; }
        public int Priority { get; set; }
        public Func<LoomContext, Task<string>> Transform { get; set; }
        public DateTimeOffset RegisteredAt { get; }
        public DateTimeOffset? LastAppliedAt { get; set; }
        public DateTimeOffset? LastFailedAt { get; set; }
        public string? LastError { get; set; }
        public LoomStatus Status { get; set; } = LoomStatus.Healthy;

        public LoomEntry(
            LoomKey key,
            string pluginVersion,
            string targetFilePath,
            int priority,
            Func<LoomContext, Task<string>> transform)
        {
            Key = key;
            PluginVersion = pluginVersion;
            TargetFilePath = targetFilePath;
            Priority = priority;
            Transform = transform;
            RegisteredAt = DateTimeOffset.UtcNow;
        }
    }
}

namespace Jellyfin.Plugin.Loom.Interfaces
{
    using Jellyfin.Plugin.Loom.Models;

    public interface ILoomRegistrar
    {
        void Register(LoomEntry entry);
        void Update(LoomEntry entry);
        bool Deregister(LoomKey key);
        IReadOnlyCollection<LoomEntry> List(string? targetFilePath = null);
        LoomEntry? GetStatus(LoomKey key);
        void InvalidateCache(string targetFilePath);
    }
}
