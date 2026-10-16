using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.Announcements;

/// <summary>Authorization for the public feed, independent of browser display preferences.</summary>
public static class AnnouncementVisibility
{
    private static bool ContainsId(IEnumerable<string> values, Guid id)
        => values.Any(value => Guid.TryParse(value, out var parsed) && parsed == id);

    public static bool CanRead(Announcement item, Guid? viewer, bool administrator, IReadOnlySet<Guid>? libraries)
    {
        if (!viewer.HasValue && !item.ShowOnLoginPage) return false;
        var included = viewer.HasValue && ContainsId(item.IncludeUserIds, viewer.Value);
        if (viewer.HasValue && ContainsId(item.ExcludeUserIds, viewer.Value)) return false;
        if (item.IncludeUserIds.Count != 0 && !included) return false;

        // Jellyfin has no native kid classification. Unknown audiences fail closed.
        var audienceAllowed = item.Audience?.ToLowerInvariant() switch
        {
            "all" or "nonkids" => true,
            "authenticated" => viewer.HasValue,
            "unauthenticated" => !viewer.HasValue,
            "admins" => viewer.HasValue && administrator,
            "nonadmins" => viewer.HasValue && !administrator,
            _ => false
        };
        if (!audienceAllowed) return false;

        bool HasRole(string role) => role.ToLowerInvariant() switch
        {
            "user" => viewer.HasValue,
            "admin" => viewer.HasValue && administrator,
            "guest" => !viewer.HasValue,
            _ => false
        };
        if (!included && ((item.IncludeUserRoles.Count > 0 && !item.IncludeUserRoles.Any(HasRole))
            || item.ExcludeUserRoles.Any(HasRole))) return false;

        if (item.IncludeLibraryIds.Count == 0 && item.ExcludeLibraryIds.Count == 0) return true;
        // Missing library context must not accidentally bypass an exclusion rule.
        if (!viewer.HasValue || libraries is null) return false;
        bool Accessible(string value) => Guid.TryParse(value, out var id) && libraries.Contains(id);
        return (item.IncludeLibraryIds.Count == 0 || item.IncludeLibraryIds.Any(Accessible))
            && !item.ExcludeLibraryIds.Any(Accessible);
    }

    /// <summary>Only display data leaves the server; targeting lists and analytics stay private.</summary>
    public static object ToPublicResponse(Announcement item, Guid? viewer)
    {
        var revision = JsonSerializer.Serialize(new
        {
            viewer, item.Id, item.Title, item.Message, item.Level, item.StartsAt, item.EndsAt,
            item.AllowDismiss, item.DismissMode, item.Priority
        });
        return new
        {
            id = item.Id, title = item.Title, message = item.Message, level = item.Level,
            startsAt = item.StartsAt, endsAt = item.EndsAt, showOnLoginPage = item.ShowOnLoginPage,
            priority = item.Priority, allowDismiss = item.AllowDismiss, dismissMode = item.DismissMode,
            includeDeviceTypes = item.IncludeDeviceTypes, excludeDeviceTypes = item.ExcludeDeviceTypes,
            dismissalKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revision)))
        };
    }
}
