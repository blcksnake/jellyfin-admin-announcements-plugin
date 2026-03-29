using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Announcements;

public class Announcement
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>"info", "warning", or "danger"</summary>
    [JsonPropertyName("level")]
    public string Level { get; set; } = "info";

    [JsonPropertyName("startsAt")]
    public DateTimeOffset StartsAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("endsAt")]
    public DateTimeOffset? EndsAt { get; set; }

    [JsonPropertyName("showOnLoginPage")]
    public bool ShowOnLoginPage { get; set; }

    /// <summary>0=Low, 1=Normal, 2=High, 3=Critical</summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 1;

    [JsonPropertyName("allowDismiss")]
    public bool AllowDismiss { get; set; } = true;

    /// <summary>"permanent" or "session".</summary>
    [JsonPropertyName("dismissMode")]
    public string DismissMode { get; set; } = "permanent";

    public bool IsActive(DateTimeOffset now) =>
        now >= StartsAt && (EndsAt is null || now <= EndsAt);
}
