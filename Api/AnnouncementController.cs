using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Mime;
using System.Reflection;
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

    private readonly AnnouncementStore _store;

    private static int PriorityFromLevel(string level)
    {
        var normalized = (level ?? "info").ToLowerInvariant();
        if (normalized == "danger") return 3;
        if (normalized == "warning") return 2;
        return 1;
    }

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

        if (string.IsNullOrWhiteSpace(announcement.Title))
            return BadRequest("Title is required.");

        if (string.IsNullOrWhiteSpace(announcement.Message))
            return BadRequest("Message is required.");

        if (string.IsNullOrWhiteSpace(announcement.Level) || !ValidLevels.Contains(announcement.Level))
            return BadRequest("Level must be one of: info, warning, danger.");

        if (announcement.EndsAt is not null && announcement.EndsAt < announcement.StartsAt)
            return BadRequest("EndsAt must be greater than or equal to StartsAt.");

        if (string.IsNullOrWhiteSpace(announcement.DismissMode))
            announcement.DismissMode = "permanent";

        if (!ValidDismissModes.Contains(announcement.DismissMode))
            return BadRequest("DismissMode must be one of: permanent, session.");

        announcement.Level = announcement.Level.ToLowerInvariant();
        announcement.DismissMode = announcement.DismissMode.ToLowerInvariant();
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
}
