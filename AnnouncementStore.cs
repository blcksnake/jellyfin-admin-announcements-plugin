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

    public IReadOnlyList<Announcement> GetActive()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync) return SortForDisplay(_items.Where(a => a.IsActive(now))).ToList();
    }

    public IReadOnlyList<Announcement> GetAll()
    {
        lock (_sync) return SortForDisplay(_items).ToList();
    }

    public Announcement AddOrUpdate(Announcement a)
    {
        lock (_sync)
        {
            var existing = _items.FirstOrDefault(x => x.Id == a.Id);
            if (existing is null)
                _items.Add(a);
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
            }
            Save();
            return a;
        }
    }

    public void Delete(string id)
    {
        lock (_sync)
        {
            _items = _items.Where(x => x.Id != id).ToList();
            Save();
        }
    }
}
