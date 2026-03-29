using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Announcements.Configuration;

/// <summary>
/// Plugin configuration saved via Jellyfin's XML config system.
/// Extend this with admin settings as needed.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets a value indicating whether the announcement banner is enabled.</summary>
    public bool EnableBanner { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether announcements are shown on the login page.</summary>
    public bool ShowOnLoginPage { get; set; } = false;
}
