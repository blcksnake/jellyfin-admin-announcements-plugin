using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.Announcements;

/// <summary>Only deployment-controlled paths may be selected by the web administrator.</summary>
public static class WebIndexPathPolicy
{
    public static bool IsApproved(string path, IEnumerable<string> deploymentCandidates)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var full = Path.GetFullPath(path);
        if (!deploymentCandidates.Any(candidate => string.Equals(Path.GetFullPath(candidate), full, comparison))) return false;
        if (!string.Equals(Path.GetFileName(full), "index.html", comparison) || !File.Exists(full)) return false;
        // Refuse links in the entire path, including directory junctions. Deployment
        // directories must remain writable only by the trusted local OS operator.
        for (FileSystemInfo? entry = new FileInfo(full); entry is not null;
            entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        }
        return true;
    }
}
