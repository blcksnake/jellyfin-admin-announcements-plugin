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

    /// <summary>Gets or sets a custom path to jellyfin-web directory (optional, for non-standard installations).</summary>
    public string? CustomWebPath { get; set; }

    /// <summary>Gets or sets a direct path to index.html (optional, overrides CustomWebPath).</summary>
    public string? CustomIndexPath { get; set; }

    /// <summary>Gets or sets a value indicating whether to enable verbose path resolution logging for debugging.</summary>
    public bool EnablePathLogging { get; set; } = false;
}
