using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Announcements;

/// <summary>Atomic, bounded abuse control. It does not make browser-reported counts trustworthy.</summary>
public sealed class AnalyticsThrottle
{
    private readonly object _sync = new();
    private readonly Dictionary<string, long> _events = new(StringComparer.Ordinal);
    private long _window;
    private int _accepted;

    public bool Allow(string key, long now)
    {
        lock (_sync)
        {
            if (now - _window >= 1000) { _window = now; _accepted = 0; }
            if (_accepted >= 100) return false;
            if (_events.TryGetValue(key, out var previous) && now - previous < 500) return false;
            if (_events.Count >= 8192)
            {
                foreach (var expired in _events.Where(pair => now - pair.Value >= 60000).Select(pair => pair.Key).ToArray())
                    _events.Remove(expired);
                if (_events.Count >= 8192 && !_events.ContainsKey(key)) return false;
            }
            _events[key] = now;
            _accepted++;
            return true;
        }
    }
}
