using MediaBrowser.Model.Plugins;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Loom.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public int StartupTimeoutSeconds { get; set; } = 10;
        public List<PriorityOverride> PriorityOverrides { get; set; } = new();
    }

    public class PriorityOverride
    {
        public string PluginId { get; set; } = string.Empty;
        public string TransformationName { get; set; } = string.Empty;
        public int Priority { get; set; }
    }
}
