using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Announcements;

/// <summary>
/// Thread-safe, JSON-backed store for announcements.
/// Registered as a singleton in Jellyfin's DI container.
/// </summary>
public class AnnouncementStore : IDisposable
{
    private readonly object _sync = new();
    private List<Announcement> _items = new();
    private readonly string _path;
    private readonly Dictionary<string, HashSet<string>> _activeImpressions = new(StringComparer.Ordinal);
    private readonly ILogger _logger;
    private string _persistedJson = "[]";
    private bool _writeBlocked;
    private bool _analyticsDirty;
    private readonly Timer _analyticsTimer;

    public AnnouncementStore(IApplicationPaths appPaths, ILogger logger)
    {
        _logger = logger;
        _path = Path.Combine(appPaths.DataPath, "announcements.json");
        Load();
        _analyticsTimer = new Timer(_ => FlushAnalytics(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) { Save(); return; }
            var json = File.ReadAllText(_path);
            _items = JsonSerializer.Deserialize<List<Announcement>>(json)
                ?? throw new JsonException("Expected an announcement array.");
            // Validate records before accepting the file as a recoverable snapshot.
            foreach (var item in _items) _ = CloneForRead(item);
            _persistedJson = json;
        }
        catch (Exception ex)
        {
            _writeBlocked = true;
            if (ex is JsonException or ArgumentException or NullReferenceException)
            {
                var recovery = _path + ".recovery-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.Copy(_path, recovery, overwrite: false);
                    _writeBlocked = false;
                    _logger.LogError(ex, "[Announcements] Invalid data preserved at {Recovery}. Starting with no announcements.", recovery);
                }
                catch (Exception backupError)
                {
                    _logger.LogError(backupError, "[Announcements] Recovery copy failed. Writes are disabled to protect {Path}.", _path);
                }
            }
            else
                _logger.LogError(ex, "[Announcements] Cannot read {Path}. Writes are disabled until the plugin is restarted after repair.", _path);
            _items = new();
        }
    }

    private void Save()
    {
        string? staging = null;
        try
        {
            if (_writeBlocked) throw new IOException("Storage is locked after an unsuccessful load.");
            var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
            staging = _path + ".pending-" + Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(staging, _path, overwrite: true);
            _persistedJson = json;
            _analyticsDirty = false;
        }
        catch (Exception ex)
        {
            _items = JsonSerializer.Deserialize<List<Announcement>>(_persistedJson)!;
            _analyticsDirty = false;
            _logger.LogError(ex, "[Announcements] Save failed for {Path}; changes were rolled back.", _path);
            throw;
        }
        finally
        {
            if (staging is not null)
            {
                try { File.Delete(staging); }
                catch (Exception ex) { _logger.LogWarning(ex, "[Announcements] Could not remove staging file {Path}.", staging); }
            }
        }
    }

    public void FlushAnalytics()
    {
        lock (_sync)
        {
            if (!_analyticsDirty) return;
            try { Save(); }
            catch (Exception ex) { _logger.LogWarning(ex, "[Announcements] Analytics flush failed; unsaved estimates were discarded."); }
        }
    }

    public void Dispose()
    {
        _analyticsTimer.Dispose();
        FlushAnalytics();
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
            Audience = source.Audience,
            IncludeDeviceTypes = new List<string>(source.IncludeDeviceTypes),
            ExcludeDeviceTypes = new List<string>(source.ExcludeDeviceTypes),
            IncludeUserRoles = new List<string>(source.IncludeUserRoles),
            ExcludeUserRoles = new List<string>(source.ExcludeUserRoles),
            IncludeUserIds = new List<string>(source.IncludeUserIds),
            ExcludeUserIds = new List<string>(source.ExcludeUserIds),
            IncludeLibraryIds = new List<string>(source.IncludeLibraryIds),
            ExcludeLibraryIds = new List<string>(source.ExcludeLibraryIds),
            IsArchived = source.IsArchived,
            IsEnabled = source.IsEnabled,
            ViewCount = source.ViewCount,
            DismissCount = source.DismissCount,
            ActiveImpressions = _activeImpressions.TryGetValue(source.Id, out var sessions) ? sessions.Count : 0
        };

        return clone;
    }

    private static List<string> NormalizeUniqueList(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return new List<string>();
        }

        return values
            .Select(static value => (value ?? string.Empty).Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> NormalizeLowerList(IEnumerable<string>? values)
        => NormalizeUniqueList(values)
            .Select(static value => value.ToLowerInvariant())
            .ToList();

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
            a.Tags = NormalizeUniqueList(a.Tags);
            a.IncludeDeviceTypes = NormalizeLowerList(a.IncludeDeviceTypes);
            a.ExcludeDeviceTypes = NormalizeLowerList(a.ExcludeDeviceTypes);
            a.IncludeUserRoles = NormalizeLowerList(a.IncludeUserRoles);
            a.ExcludeUserRoles = NormalizeLowerList(a.ExcludeUserRoles);
            a.IncludeUserIds = NormalizeUniqueList(a.IncludeUserIds);
            a.ExcludeUserIds = NormalizeUniqueList(a.ExcludeUserIds);
            a.IncludeLibraryIds = NormalizeUniqueList(a.IncludeLibraryIds);
            a.ExcludeLibraryIds = NormalizeUniqueList(a.ExcludeLibraryIds);
            var existing = _items.FirstOrDefault(x => x.Id == a.Id);
            if (existing is null)
            {
                a.ViewCount = 0;
                a.DismissCount = 0;
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
                existing.Audience = a.Audience;
                existing.IncludeDeviceTypes = new List<string>(a.IncludeDeviceTypes);
                existing.ExcludeDeviceTypes = new List<string>(a.ExcludeDeviceTypes);
                existing.IncludeUserRoles = new List<string>(a.IncludeUserRoles);
                existing.ExcludeUserRoles = new List<string>(a.ExcludeUserRoles);
                existing.IncludeUserIds = new List<string>(a.IncludeUserIds);
                existing.ExcludeUserIds = new List<string>(a.ExcludeUserIds);
                existing.IncludeLibraryIds = new List<string>(a.IncludeLibraryIds);
                existing.ExcludeLibraryIds = new List<string>(a.ExcludeLibraryIds);
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
                Audience = source.Audience,
                IncludeDeviceTypes = new List<string>(source.IncludeDeviceTypes),
                ExcludeDeviceTypes = new List<string>(source.ExcludeDeviceTypes),
                IncludeUserRoles = new List<string>(source.IncludeUserRoles),
                ExcludeUserRoles = new List<string>(source.ExcludeUserRoles),
                IncludeUserIds = new List<string>(source.IncludeUserIds),
                ExcludeUserIds = new List<string>(source.ExcludeUserIds),
                IncludeLibraryIds = new List<string>(source.IncludeLibraryIds),
                ExcludeLibraryIds = new List<string>(source.ExcludeLibraryIds),
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
            if (item.ViewCount < int.MaxValue) item.ViewCount++;
            _analyticsDirty = true;
        }
    }

    private const int MaxImpressionsPerAnnouncement = 10_000;

    /// <summary>Tracks a browser session as actively displaying an announcement.</summary>
    public void StartImpression(string id, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(sessionId) ||
            id.Length > 64 || sessionId.Length > 128)
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

            if (sessions.Count < MaxImpressionsPerAnnouncement)
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
            if (item.DismissCount < int.MaxValue) item.DismissCount++;
            _analyticsDirty = true;
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
