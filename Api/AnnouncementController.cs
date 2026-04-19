using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Announcements.Api;

/// <summary>
/// REST controller registered in Jellyfin via its built-in ASP.NET Core pipeline.
/// All routes appear under /Plugins/Announcements/ in Jellyfin's API.
/// </summary>
[ApiController]
[Route("Plugins/Announcements")]
[Produces(MediaTypeNames.Application.Json)]
public class AnnouncementController : ControllerBase
{
    public class DisplaySettingsDto
    {
        public bool ShowOnLoginPage { get; set; }
    }

    public class PathConfigDto
    {
        public string? CustomWebPath { get; set; }
        public string? CustomIndexPath { get; set; }
        public bool EnablePathLogging { get; set; }
    }

    public class DiagnosticsDto
    {
        [JsonPropertyName("injectionMode")]
        public string InjectionMode { get; set; } = "None";

        [JsonPropertyName("resolvedIndexPath")]
        public string? ResolvedIndexPath { get; set; }

        [JsonPropertyName("startupTime")]
        public DateTimeOffset StartupTime { get; set; }

        [JsonPropertyName("pluginVersion")]
        public string PluginVersion { get; set; } = string.Empty;
    }

    public class ImpressionDto
    {
        public string SessionId { get; set; } = string.Empty;
    }

    public class ToggleDto
    {
        public bool Value { get; set; }
    }

    private static readonly HashSet<string> ValidLevels = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "info",
        "warning",
        "danger"
    };

    private static readonly HashSet<string> ValidDismissModes = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "permanent",
        "session"
    };

    private static readonly HashSet<string> ValidAudiences = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "all",
        "authenticated",
        "unauthenticated",
        "admins",
        "nonadmins",
        "kids",
        "nonkids"
    };

    private static readonly HashSet<string> ValidDeviceTypes = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "desktop",
        "mobile",
        "tablet",
        "tv"
    };

    private static readonly HashSet<string> ValidUserRoles = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "admin",
        "user",
        "kid"
    };

    private readonly AnnouncementStore _store;

    // Per-ID+IP throttle for anonymous analytics endpoints (prevent abuse / memory exhaustion)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _analyticsTicks = new();
    private const int AnalyticsMinIntervalMs = 500;

    private bool AllowAnalytics(string id)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = id + "|" + ip;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var last = _analyticsTicks.GetOrAdd(key, 0L);
        if (now - last < AnalyticsMinIntervalMs) return false;
        _analyticsTicks[key] = now;
        return true;
    }

    private static int PriorityFromLevel(string level)
    {
        var normalized = (level ?? "info").ToLowerInvariant();
        if (normalized == "danger") return 3;
        if (normalized == "warning") return 2;
        return 1;
    }

    private static List<string> NormalizeList(IEnumerable<string>? values, bool toLower = false)
    {
        if (values is null)
        {
            return new List<string>();
        }

        var normalized = values
            .Select(static value => (value ?? string.Empty).Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!toLower)
        {
            return normalized;
        }

        return normalized.Select(static value => value.ToLowerInvariant()).ToList();
    }

    private static bool ContainsOnlyAllowed(IEnumerable<string> values, HashSet<string> allowed)
        => values.All(allowed.Contains);

    public AnnouncementController()
    {
        _store = Plugin.Instance?.Store
            ?? throw new InvalidOperationException("Announcements plugin is not loaded.");
    }

    /// <summary>Returns only active announcements (for users/client apps).</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<Announcement>> GetActive()
        => Ok(_store.GetActive());

    /// <summary>Returns all announcements including inactive (admin only).</summary>
    [HttpGet("Admin")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<Announcement>> GetAll()
        => Ok(_store.GetAll());

    /// <summary>Creates or updates an announcement (admin only).</summary>
    [HttpPost("Admin")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Announcement>> Upsert([FromBody] Announcement? announcement)
    {
        await Task.CompletedTask;
        if (announcement is null)
            return BadRequest("Request body is required.");

        announcement.Title = announcement.Title?.Trim() ?? string.Empty;
        announcement.Message = announcement.Message?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(announcement.Title))
            return BadRequest("Title is required.");

        if (string.IsNullOrWhiteSpace(announcement.Message))
            return BadRequest("Message is required.");

        if (announcement.Title.Length > 200)
            return BadRequest("Title must be 200 characters or fewer.");

        if (announcement.Message.Length > 5000)
            return BadRequest("Message must be 5000 characters or fewer.");

        if ((announcement.Tags?.Count ?? 0) > 50)
            return BadRequest("Maximum 50 tags allowed.");

        if ((announcement.IncludeUserIds?.Count ?? 0) > 500 || (announcement.ExcludeUserIds?.Count ?? 0) > 500)
            return BadRequest("Maximum 500 user ID entries per list.");

        if ((announcement.IncludeLibraryIds?.Count ?? 0) > 100 || (announcement.ExcludeLibraryIds?.Count ?? 0) > 100)
            return BadRequest("Maximum 100 library ID entries per list.");

        if (string.IsNullOrWhiteSpace(announcement.Level) || !ValidLevels.Contains(announcement.Level))
            return BadRequest("Level must be one of: info, warning, danger.");

        if (announcement.EndsAt is not null && announcement.EndsAt < announcement.StartsAt)
            return BadRequest("EndsAt must be greater than or equal to StartsAt.");

        if (string.IsNullOrWhiteSpace(announcement.DismissMode))
            announcement.DismissMode = "permanent";

        if (!ValidDismissModes.Contains(announcement.DismissMode))
            return BadRequest("DismissMode must be one of: permanent, session.");

        if (string.IsNullOrWhiteSpace(announcement.Audience))
            announcement.Audience = "all";

        if (!ValidAudiences.Contains(announcement.Audience))
            return BadRequest("Audience must be one of: all, authenticated, unauthenticated, admins, nonadmins, kids, nonkids.");

        var includeDeviceTypes = NormalizeList(announcement.IncludeDeviceTypes, toLower: true);
        var excludeDeviceTypes = NormalizeList(announcement.ExcludeDeviceTypes, toLower: true);
        var includeUserRoles = NormalizeList(announcement.IncludeUserRoles, toLower: true);
        var excludeUserRoles = NormalizeList(announcement.ExcludeUserRoles, toLower: true);

        if (!ContainsOnlyAllowed(includeDeviceTypes, ValidDeviceTypes) || !ContainsOnlyAllowed(excludeDeviceTypes, ValidDeviceTypes))
            return BadRequest("Device targeting values must be from: desktop, mobile, tablet, tv.");

        if (!ContainsOnlyAllowed(includeUserRoles, ValidUserRoles) || !ContainsOnlyAllowed(excludeUserRoles, ValidUserRoles))
            return BadRequest("User role targeting values must be from: admin, user, kid.");

        announcement.IncludeDeviceTypes = includeDeviceTypes;
        announcement.ExcludeDeviceTypes = excludeDeviceTypes;
        announcement.IncludeUserRoles = includeUserRoles;
        announcement.ExcludeUserRoles = excludeUserRoles;
        announcement.IncludeUserIds = NormalizeList(announcement.IncludeUserIds);
        announcement.ExcludeUserIds = NormalizeList(announcement.ExcludeUserIds);
        announcement.IncludeLibraryIds = NormalizeList(announcement.IncludeLibraryIds);
        announcement.ExcludeLibraryIds = NormalizeList(announcement.ExcludeLibraryIds);

        announcement.Level = announcement.Level.ToLowerInvariant();
        announcement.DismissMode = announcement.DismissMode.ToLowerInvariant();
        announcement.Audience = announcement.Audience.ToLowerInvariant();
        announcement.Priority = PriorityFromLevel(announcement.Level);
        return Ok(_store.AddOrUpdate(announcement));
    }

    /// <summary>Deletes an announcement by ID (admin only).</summary>
    [HttpDelete("Admin/{id}")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Delete(string id)
    {
        _store.Delete(id);
        return NoContent();
    }

    /// <summary>Gets display settings used by banner.js.</summary>
    [HttpGet("Settings")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<DisplaySettingsDto> GetSettings()
    {
        var showOnLogin = Plugin.Instance?.ShowOnLoginPage ?? false;
        return Ok(new DisplaySettingsDto { ShowOnLoginPage = showOnLogin });
    }

    /// <summary>Updates display settings (admin only).</summary>
    [HttpPost("Admin/Settings")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<DisplaySettingsDto> SaveSettings([FromBody] DisplaySettingsDto? settings)
    {
        if (settings is null)
        {
            return BadRequest("Request body is required.");
        }

        if (Plugin.Instance is null)
        {
            return BadRequest("Plugin is not loaded.");
        }

        Plugin.Instance.SetShowOnLoginPage(settings.ShowOnLoginPage);
        return Ok(new DisplaySettingsDto { ShowOnLoginPage = Plugin.Instance.ShowOnLoginPage });
    }

    /// <summary>Serves the injected banner script for JS Injector.</summary>
    [HttpGet("banner.js")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetBannerScript()
    {
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Jellyfin.Plugin.Announcements.Web.announcement.js");
        if (stream is null)
            return NotFound();
        return File(stream, "application/javascript");
    }

    /// <summary>Serves banner styling for the injected script.</summary>
    [HttpGet("banner.css")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetBannerStyles()
    {
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Jellyfin.Plugin.Announcements.Web.announcement.css");
        if (stream is null)
            return NotFound();
        return File(stream, "text/css");
    }

    /// <summary>Gets path configuration for troubleshooting jellyfin-web detection.</summary>
    [HttpGet("Admin/PathConfig")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PathConfigDto> GetPathConfig()
    {
        if (Plugin.Instance is null)
        {
            return BadRequest("Plugin is not loaded.");
        }

        return Ok(new PathConfigDto
        {
            CustomWebPath = Plugin.Instance.GetCustomWebPath(),
            CustomIndexPath = Plugin.Instance.GetCustomIndexPath(),
            EnablePathLogging = Plugin.Instance.GetEnablePathLogging()
        });
    }

    /// <summary>Updates path configuration for troubleshooting jellyfin-web detection.</summary>
    [HttpPost("Admin/PathConfig")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<PathConfigDto> SavePathConfig([FromBody] PathConfigDto? settings)
    {
        if (settings is null)
        {
            return BadRequest("Request body is required.");
        }

        if (Plugin.Instance is null)
        {
            return BadRequest("Plugin is not loaded.");
        }

        Plugin.Instance.SetCustomWebPath(settings.CustomWebPath);
        Plugin.Instance.SetCustomIndexPath(settings.CustomIndexPath);
        Plugin.Instance.SetEnablePathLogging(settings.EnablePathLogging);

        return Ok(new PathConfigDto
        {
            CustomWebPath = Plugin.Instance.GetCustomWebPath(),
            CustomIndexPath = Plugin.Instance.GetCustomIndexPath(),
            EnablePathLogging = Plugin.Instance.GetEnablePathLogging()
        });
    }

    /// <summary>Returns operational diagnostics: injection mode, resolved path, startup time.</summary>
    [HttpGet("Admin/Diagnostics")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<DiagnosticsDto> GetDiagnostics()
    {
        if (Plugin.Instance is null)
        {
            return BadRequest("Plugin is not loaded.");
        }

        return Ok(Plugin.Instance.GetDiagnostics());
    }

    // ── Analytics endpoints (anonymous — fire-and-forget from banner.js) ──────────

    /// <summary>Records that a user viewed an announcement. Idempotent; ignores unknown IDs.</summary>
    [HttpPost("{id}/View")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult RecordView(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return NoContent();
        if (!AllowAnalytics(id)) return StatusCode(StatusCodes.Status429TooManyRequests);
        _store.RecordView(id);
        return NoContent();
    }

    /// <summary>Records that a user dismissed an announcement. Idempotent; ignores unknown IDs.</summary>
    [HttpPost("{id}/Dismiss")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult RecordDismiss(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return NoContent();
        if (!AllowAnalytics(id)) return StatusCode(StatusCodes.Status429TooManyRequests);
        _store.RecordDismiss(id);
        return NoContent();
    }

    /// <summary>Marks an announcement as currently visible in a browser session.</summary>
    [HttpPost("{id}/Impression/Start")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult StartImpression(string id, [FromBody] ImpressionDto? dto)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return NoContent();
        if (string.IsNullOrWhiteSpace(dto?.SessionId) || dto.SessionId.Length > 128) return NoContent();
        if (!AllowAnalytics(id)) return StatusCode(StatusCodes.Status429TooManyRequests);
        _store.StartImpression(id, dto.SessionId);
        return NoContent();
    }

    /// <summary>Marks an announcement as no longer visible in a browser session.</summary>
    [HttpPost("{id}/Impression/End")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult EndImpression(string id, [FromBody] ImpressionDto? dto)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return NoContent();
        if (string.IsNullOrWhiteSpace(dto?.SessionId) || dto.SessionId.Length > 128) return NoContent();
        if (!AllowAnalytics(id)) return StatusCode(StatusCodes.Status429TooManyRequests);
        _store.EndImpression(id, dto.SessionId);
        return NoContent();
    }

    // ── Admin QoL endpoints ────────────────────────────────────────────────────────

    /// <summary>Duplicates an existing announcement with a new ID, retaining all fields. The copy starts disabled.</summary>
    [HttpPost("Admin/{id}/Duplicate")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Announcement> Duplicate(string id)
    {
        var copy = _store.Duplicate(id);
        if (copy is null)
            return NotFound();
        return Ok(copy);
    }

    /// <summary>Archives or unarchives an announcement.</summary>
    [HttpPost("Admin/{id}/Archive")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult SetArchived(string id, [FromBody] ToggleDto? dto)
    {
        var archived = dto?.Value ?? true;
        if (!_store.SetArchived(id, archived))
            return NotFound();
        return NoContent();
    }

    /// <summary>Enables or disables (pauses) an announcement.</summary>
    [HttpPost("Admin/{id}/Enable")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult SetEnabled(string id, [FromBody] ToggleDto? dto)
    {
        var enabled = dto?.Value ?? true;
        if (!_store.SetEnabled(id, enabled))
            return NotFound();
        return NoContent();
    }
}
