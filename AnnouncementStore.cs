using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Announcements;

/// <summary>
/// Thread-safe, JSON-backed store for announcements.
/// Registered as a singleton in Jellyfin's DI container.
/// </summary>
public class AnnouncementStore
{
    private readonly object _sync = new();
    private List<Announcement> _items = new();
    private readonly string _path;
    private readonly Dictionary<string, HashSet<string>> _activeImpressions = new(StringComparer.Ordinal);

    public AnnouncementStore(IApplicationPaths appPaths)
    {
        _path = Path.Combine(appPaths.DataPath, "announcements.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) { Save(); return; }
            var json = File.ReadAllText(_path);
            _items = JsonSerializer.Deserialize<List<Announcement>>(json) ?? new();
        }
        catch { _items = new(); }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch { }
    }

    private static IEnumerable<Announcement> SortForDisplay(IEnumerable<Announcement> source)
    {
        static int SeverityRank(string level)
            => string.Equals(level, "danger", StringComparison.OrdinalIgnoreCase) ? 3
            : string.Equals(level, "warning", StringComparison.OrdinalIgnoreCase) ? 2
            : 1;

        return source
            .OrderByDescending(a => SeverityRank(a.Level))
            .ThenByDescending(a => a.StartsAt);
    }

    private Announcement CloneForRead(Announcement source)
    {
        var clone = new Announcement
        {
            Id = source.Id,
            Title = source.Title,
            Message = source.Message,
            Level = source.Level,
            StartsAt = source.StartsAt,
            EndsAt = source.EndsAt,
            ShowOnLoginPage = source.ShowOnLoginPage,
            Priority = source.Priority,
            AllowDismiss = source.AllowDismiss,
            DismissMode = source.DismissMode,
            Tags = new List<string>(source.Tags),
            IsArchived = source.IsArchived,
            IsEnabled = source.IsEnabled,
            ViewCount = source.ViewCount,
            DismissCount = source.DismissCount,
            ActiveImpressions = _activeImpressions.TryGetValue(source.Id, out var sessions) ? sessions.Count : 0
        };

        return clone;
    }

    private static List<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return new List<string>();
        }

        return tags
            .Select(static tag => (tag ?? string.Empty).Trim())
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<Announcement> GetActive()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync) return SortForDisplay(_items.Where(a => a.IsActive(now)).Select(CloneForRead)).ToList();
    }

    public IReadOnlyList<Announcement> GetAll()
    {
        lock (_sync) return SortForDisplay(_items.Select(CloneForRead)).ToList();
    }

    public Announcement AddOrUpdate(Announcement a)
    {
        lock (_sync)
        {
            a.Tags = NormalizeTags(a.Tags);
            var existing = _items.FirstOrDefault(x => x.Id == a.Id);
            if (existing is null)
            {
                _items.Add(a);
            }
            else
            {
                existing.Title = a.Title;
                existing.Message = a.Message;
                existing.Level = a.Level;
                existing.StartsAt = a.StartsAt;
                existing.EndsAt = a.EndsAt;
                existing.ShowOnLoginPage = a.ShowOnLoginPage;
                existing.Priority = a.Priority;
                existing.AllowDismiss = a.AllowDismiss;
                existing.DismissMode = a.DismissMode;
                existing.Tags = new List<string>(a.Tags);
                existing.IsArchived = a.IsArchived;
                existing.IsEnabled = a.IsEnabled;
                // Preserve analytics counters — never overwrite from client payload.
                a.ViewCount = existing.ViewCount;
                a.DismissCount = existing.DismissCount;
                a.ActiveImpressions = _activeImpressions.TryGetValue(existing.Id, out var sessions) ? sessions.Count : 0;
            }
            Save();
            return CloneForRead(existing ?? a);
        }
    }

    /// <summary>Creates a copy of the given announcement with a new ID and a "(Copy)" title suffix.</summary>
    public Announcement? Duplicate(string id)
    {
        lock (_sync)
        {
            var source = _items.FirstOrDefault(x => x.Id == id);
            if (source is null) return null;

            var copy = new Announcement
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = source.Title + " (Copy)",
                Message = source.Message,
                Level = source.Level,
                StartsAt = source.StartsAt,
                EndsAt = source.EndsAt,
                ShowOnLoginPage = source.ShowOnLoginPage,
                Priority = source.Priority,
                AllowDismiss = source.AllowDismiss,
                DismissMode = source.DismissMode,
                Tags = new List<string>(source.Tags),
                IsArchived = false,
                IsEnabled = false,
                ViewCount = 0,
                DismissCount = 0,
                ActiveImpressions = 0
            };
            _items.Add(copy);
            Save();
            return CloneForRead(copy);
        }
    }

    /// <summary>Sets the archived state of an announcement.</summary>
    public bool SetArchived(string id, bool archived)
    {
        lock (_sync)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item is null) return false;
            item.IsArchived = archived;
            if (archived)
            {
                _activeImpressions.Remove(id);
            }
            Save();
            return true;
        }
    }

    /// <summary>Sets the enabled (active/paused) state of an announcement.</summary>
    public bool SetEnabled(string id, bool enabled)
    {
        lock (_sync)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item is null) return false;
            item.IsEnabled = enabled;
            if (!enabled)
            {
                _activeImpressions.Remove(id);
            }
            Save();
            return true;
        }
    }

    /// <summary>Increments the view counter for an announcement. No-ops on unknown IDs.</summary>
    public void RecordView(string id)
    {
        lock (_sync)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item is null) return;
            item.ViewCount++;
            Save();
        }
    }

    /// <summary>Tracks a browser session as actively displaying an announcement.</summary>
    public void StartImpression(string id, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sync)
        {
            if (!_items.Any(x => x.Id == id))
            {
                return;
            }

            if (!_activeImpressions.TryGetValue(id, out var sessions))
            {
                sessions = new HashSet<string>(StringComparer.Ordinal);
                _activeImpressions[id] = sessions;
            }

            sessions.Add(sessionId);
        }
    }

    /// <summary>Stops tracking a browser session as displaying an announcement.</summary>
    public void EndImpression(string id, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sync)
        {
            if (!_activeImpressions.TryGetValue(id, out var sessions))
            {
                return;
            }

            sessions.Remove(sessionId);
            if (sessions.Count == 0)
            {
                _activeImpressions.Remove(id);
            }
        }
    }

    /// <summary>Increments the dismiss counter for an announcement. No-ops on unknown IDs.</summary>
    public void RecordDismiss(string id)
    {
        lock (_sync)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item is null) return;
            item.DismissCount++;
            Save();
        }
    }

    public void Delete(string id)
    {
        lock (_sync)
        {
            _items = _items.Where(x => x.Id != id).ToList();
            _activeImpressions.Remove(id);
            Save();
        }
    }
}
