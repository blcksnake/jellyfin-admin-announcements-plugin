using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Announcements.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.Announcements;

/// <summary>
/// Main plugin entry point. Jellyfin discovers this via MEF/DI.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ILogger<Plugin> _logger;
    private readonly string _runtimeSettingsPath;
    private RuntimeSettings _runtimeSettings;
    private int _retryCount;
    private const int MaxRetries = 3;

    private sealed class RuntimeSettings
    {
        public bool ShowOnLoginPage { get; set; }
    }

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        _logger = logger;
        Instance = this;
        Store = new AnnouncementStore(applicationPaths);
        _runtimeSettingsPath = Path.Combine(applicationPaths.DataPath, "announcements.settings.json");
        _runtimeSettings = LoadRuntimeSettings();
        var indexPatched = PatchJellyfinWebIndex();
        var jsInjectorRegistered = RegisterWithJsInjector();

        if (!indexPatched && !jsInjectorRegistered)
        {
            _logger.LogError(
                "[Announcements] Banner script was not injected. Install/enable JavaScript Injector, or set JELLYFIN_WEB_INDEX_PATH/JELLYFIN_WEB_DIR to a writable jellyfin-web index path.");
        }
    }

    public static Plugin? Instance { get; private set; }

    /// <summary>The announcement data store (singleton owned by this plugin).</summary>
    public AnnouncementStore Store { get; }

    /// <summary>Gets a value indicating whether announcements are shown on the login page.</summary>
    public bool ShowOnLoginPage => _runtimeSettings.ShowOnLoginPage;

    /// <summary>Updates whether announcements should appear on the login page.</summary>
    public void SetShowOnLoginPage(bool enabled)
    {
        if (_runtimeSettings.ShowOnLoginPage == enabled)
        {
            return;
        }

        _runtimeSettings.ShowOnLoginPage = enabled;
        SaveRuntimeSettings();
    }

    /// <summary>Gets current custom web path configuration.</summary>
    public string? GetCustomWebPath() => Configuration.CustomWebPath;

    /// <summary>Sets custom web path configuration.</summary>
    public void SetCustomWebPath(string? path)
    {
        if (Configuration.CustomWebPath == path)
        {
            return;
        }

        Configuration.CustomWebPath = path;
        SaveConfiguration();
    }

    /// <summary>Gets current custom index path configuration.</summary>
    public string? GetCustomIndexPath() => Configuration.CustomIndexPath;

    /// <summary>Sets custom index path configuration.</summary>
    public void SetCustomIndexPath(string? path)
    {
        if (Configuration.CustomIndexPath == path)
        {
            return;
        }

        Configuration.CustomIndexPath = path;
        SaveConfiguration();
    }

    /// <summary>Gets whether verbose path logging is enabled.</summary>
    public bool GetEnablePathLogging() => Configuration.EnablePathLogging;

    /// <summary>Sets whether verbose path logging is enabled.</summary>
    public void SetEnablePathLogging(bool enabled)
    {
        if (Configuration.EnablePathLogging == enabled)
        {
            return;
        }

        Configuration.EnablePathLogging = enabled;
        SaveConfiguration();
    }

    public override string Name => "Announcements";

    public override Guid Id => Guid.Parse("b0f1f4f0-3f5a-4a9b-9a0b-a11c0ce00001");

    public override string Description => "Server-wide announcements and maintenance banners for Jellyfin.";

    private RuntimeSettings LoadRuntimeSettings()
    {
        try
        {
            if (!File.Exists(_runtimeSettingsPath))
            {
                var defaults = new RuntimeSettings { ShowOnLoginPage = false };
                File.WriteAllText(_runtimeSettingsPath, JsonSerializer.Serialize(defaults));
                return defaults;
            }

            var json = File.ReadAllText(_runtimeSettingsPath);
            return JsonSerializer.Deserialize<RuntimeSettings>(json) ?? new RuntimeSettings { ShowOnLoginPage = false };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Announcements] Failed to load runtime settings. Using defaults.");
            return new RuntimeSettings { ShowOnLoginPage = false };
        }
    }

    private void SaveRuntimeSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_runtimeSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_runtimeSettingsPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Announcements] Failed to save runtime settings.");
        }
    }

    /// <summary>
    /// Exposes the embedded admin config page to the Jellyfin web UI.
    /// </summary>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "Announcements",
            EmbeddedResourcePath = GetType().Namespace + ".Web.configPage.html",
            EnableInMainMenu = true,
            DisplayName = "Announcements",
            MenuIcon = "notifications"
        };
    }

    private bool PatchJellyfinWebIndex()
    {
        const string ScriptTag = "<script src=\"/Plugins/Announcements/banner.js\" defer></script>";
        const string Marker = "Plugins/Announcements/banner.js";
        var enableDebugLogging = Configuration.EnablePathLogging;

        // Build a cross-platform candidate list so production container paths are covered.
        var candidates = BuildIndexCandidates().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (enableDebugLogging)
        {
            _logger.LogInformation("[Announcements] DEBUG: Searching for index.html in {Count} candidate paths", candidates.Length);
            foreach (var candidate in candidates)
            {
                _logger.LogInformation("[Announcements] DEBUG: Candidate path: {Path}", candidate);
            }
        }

        foreach (var indexPath in candidates)
        {
            try
            {
                var full = Path.GetFullPath(indexPath);
                
                if (!File.Exists(full))
                {
                    if (enableDebugLogging)
                        _logger.LogDebug("[Announcements] DEBUG: File not found: {Path}", full);
                    continue;
                }

                if (enableDebugLogging)
                    _logger.LogInformation("[Announcements] DEBUG: Found index.html at {Path}", full);

                var content = File.ReadAllText(full, System.Text.Encoding.UTF8);
                if (content.Contains(Marker))
                {
                    _logger.LogInformation("[Announcements] index.html already patched at {Path}", full);
                    return true;
                }

                // Back up once
                var bak = full + ".bak";
                if (!File.Exists(bak)) File.Copy(full, bak);

                content = content.Replace("</body>", ScriptTag + "\n</body>", StringComparison.OrdinalIgnoreCase);
                File.WriteAllText(full, content, System.Text.Encoding.UTF8);
                _logger.LogInformation("[Announcements] Patched {Path} — banners will auto-load on every page.", full);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Announcements] Could not patch index.html at {Path}", indexPath);
            }
        }

        _logger.LogWarning(
            "[Announcements] jellyfin-web/index.html not found. To fix: 1) Install JS Injector plugin, 2) Set env vars JELLYFIN_WEB_INDEX_PATH or JELLYFIN_WEB_DIR, 3) Use plugin config settings for custom paths, or 4) Enable path logging (EnablePathLogging=true in plugin config) to debug.");
        return false;
    }

    private IEnumerable<string> BuildIndexCandidates()
    {
        var candidates = new List<string>();

        void Add(params string[] parts)
        {
            if (parts.Length == 0)
            {
                return;
            }

            candidates.Add(Path.Combine(parts));
        }

        // Priority 1: Direct path from plugin configuration (highest priority)
        if (!string.IsNullOrWhiteSpace(Configuration.CustomIndexPath))
        {
            candidates.Add(Configuration.CustomIndexPath);
        }

        // Priority 2: Custom web directory from plugin configuration
        if (!string.IsNullOrWhiteSpace(Configuration.CustomWebPath))
        {
            Add(Configuration.CustomWebPath, "index.html");
        }

        // Priority 3: Environment variables (JELLYFIN_WEB_INDEX_PATH takes precedence over CustomIndexPath from env)
        var envIndex = Environment.GetEnvironmentVariable("JELLYFIN_WEB_INDEX_PATH");
        if (!string.IsNullOrWhiteSpace(envIndex))
        {
            candidates.Add(envIndex);
        }

        var envWebDir = Environment.GetEnvironmentVariable("JELLYFIN_WEB_DIR");
        if (!string.IsNullOrWhiteSpace(envWebDir))
        {
            Add(envWebDir, "index.html");
        }

        // Priority 4: Relative paths from application directory
        Add(AppContext.BaseDirectory, "jellyfin-web", "index.html");
        Add(AppContext.BaseDirectory, "jellyfin-web", "dist", "index.html");
        Add(AppContext.BaseDirectory, "web", "index.html");
        Add(AppContext.BaseDirectory, "web", "dist", "index.html");
        Add(AppContext.BaseDirectory, "..", "jellyfin-web", "index.html");
        Add(AppContext.BaseDirectory, "..", "jellyfin-web", "dist", "index.html");
        Add(AppContext.BaseDirectory, "..", "web", "index.html");
        Add(AppContext.BaseDirectory, "..", "web", "dist", "index.html");

        // Priority 5: ASPNETCORE_CONTENTROOT environment variable
        var contentRoot = Environment.GetEnvironmentVariable("ASPNETCORE_CONTENTROOT");
        if (!string.IsNullOrWhiteSpace(contentRoot))
        {
            Add(contentRoot, "jellyfin-web", "index.html");
            Add(contentRoot, "jellyfin-web", "dist", "index.html");
            Add(contentRoot, "web", "index.html");
            Add(contentRoot, "web", "dist", "index.html");
        }

        // Priority 6: Windows standard installation paths
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                Add(programFiles, "Jellyfin", "Server", "jellyfin-web", "index.html");
                Add(programFiles, "Jellyfin", "Server", "web", "index.html");
            }

            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                Add(programFilesX86, "Jellyfin", "Server", "jellyfin-web", "index.html");
                Add(programFilesX86, "Jellyfin", "Server", "web", "index.html");
            }
        }
        else
        {
            // Priority 7: Common Linux/macOS and container locations.
            Add("/usr/share/jellyfin/web/index.html");
            Add("/usr/share/jellyfin/jellyfin-web/index.html");
            Add("/usr/lib/jellyfin/bin/jellyfin-web/index.html");
            Add("/usr/lib/jellyfin/bin/web/index.html");
            Add("/jellyfin/jellyfin-web/index.html");
            Add("/jellyfin/web/index.html");
            Add("/opt/jellyfin/web/index.html");
            Add("/opt/jellyfin/jellyfin-web/index.html");
            Add("/opt/jellyfin/jellyfin/web/index.html");
            Add("/Applications/Jellyfin.app/Contents/Resources/jellyfin-web/index.html");
            Add("/Applications/Jellyfin.app/Contents/Resources/web/index.html");
        }

        return candidates;
    }

    private bool RegisterWithJsInjector()
    {
        try
        {
            var jsInjectorAssembly = AssemblyLoadContext.All
                .SelectMany(ctx => ctx.Assemblies)
                .FirstOrDefault(a => a.FullName?.Contains("Jellyfin.Plugin.JavaScriptInjector") ?? false);

            if (jsInjectorAssembly is null)
            {
                _logger.LogInformation("[Announcements] JS Injector plugin not found; banner auto-injection disabled.");
                return false;
            }

            var pluginInterfaceType = jsInjectorAssembly.GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
            if (pluginInterfaceType is null)
            {
                _logger.LogWarning("[Announcements] JS Injector PluginInterface type not found.");
                return false;
            }

            var resourceName = "Jellyfin.Plugin.Announcements.Web.announcement.js";
            string scriptContent;
            using (var stream = GetType().Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream is null)
                {
                    _logger.LogWarning("[Announcements] Embedded resource '{Resource}' not found.", resourceName);
                    return false;
                }

                using var reader = new StreamReader(stream);
                scriptContent = reader.ReadToEnd();
            }

            var payload = new JObject
            {
                { "id", $"{Id}-banner-script" },
                { "name", "Announcements Banner Script" },
                { "script", scriptContent },
                { "enabled", true },
                { "requiresAuthentication", true },
                { "pluginId", Id.ToString() },
                { "pluginName", Name },
                { "pluginVersion", Version?.ToString() ?? "1.0.0" }
            };

            pluginInterfaceType.GetMethod("RegisterScript")?.Invoke(null, new object?[] { payload });
            _logger.LogInformation("[Announcements] Banner script registered with JS Injector.");
            return true;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            if (_retryCount >= MaxRetries)
            {
                _logger.LogWarning("[Announcements] JS Injector not ready after {MaxRetries} retries.", MaxRetries);
                return false;
            }

            _retryCount++;
            _logger.LogInformation("[Announcements] JS Injector not ready; retrying in 5s ({Attempt}/{MaxRetries}).", _retryCount, MaxRetries);
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                RegisterWithJsInjector();
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Announcements] Failed to register script with JS Injector.");
            return false;
        }
    }
}
