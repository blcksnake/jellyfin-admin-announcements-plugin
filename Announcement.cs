using System;
using System.Collections.Generic;
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

    /// <summary>Categorization tags for filtering and grouping.</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>When true, the announcement is hidden from all views and API responses.</summary>
    [JsonPropertyName("isArchived")]
    public bool IsArchived { get; set; }

    /// <summary>When false, the announcement is paused and will not appear regardless of schedule.</summary>
    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>Total number of times this announcement has been rendered to users.</summary>
    [JsonPropertyName("viewCount")]
    public int ViewCount { get; set; }

    /// <summary>Total number of times this announcement has been dismissed by users.</summary>
    [JsonPropertyName("dismissCount")]
    public int DismissCount { get; set; }

    /// <summary>Current number of active browser sessions displaying this announcement.</summary>
    [JsonPropertyName("activeImpressions")]
    public int ActiveImpressions { get; set; }

    public bool IsActive(DateTimeOffset now) =>
        !IsArchived && IsEnabled && now >= StartsAt && (EndsAt is null || now <= EndsAt);
}
