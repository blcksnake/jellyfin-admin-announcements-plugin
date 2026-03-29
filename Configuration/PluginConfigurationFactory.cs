using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Announcements.Configuration;

/// <summary>
/// Registers the plugin configuration with Jellyfin's config system.
/// </summary>
public class PluginConfigurationFactory : IConfigurationFactory
{
    public PluginConfigurationFactory()
    {
    }

    public IEnumerable<ConfigurationStore> GetConfigurations()
    {
        yield return new ConfigurationStore
        {
            ConfigurationType = typeof(PluginConfiguration),
            Key = "announcements"
        };
    }
}
