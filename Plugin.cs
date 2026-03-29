using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        PatchJellyfinWebIndex();
        RegisterWithJsInjector();
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

    private void PatchJellyfinWebIndex()
    {
        const string ScriptTag = "<script src=\"/Plugins/Announcements/banner.js\" defer></script>";
        const string Marker = "Plugins/Announcements/banner.js";

        // Search well-known locations for jellyfin-web/index.html
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "jellyfin-web", "index.html"),
            Path.Combine(AppContext.BaseDirectory, "..", "jellyfin-web", "index.html"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Jellyfin", "Server", "jellyfin-web", "index.html"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Jellyfin", "Server", "jellyfin-web", "index.html"),
        };

        foreach (var indexPath in candidates)
        {
            try
            {
                var full = Path.GetFullPath(indexPath);
                if (!File.Exists(full)) continue;

                var content = File.ReadAllText(full, System.Text.Encoding.UTF8);
                if (content.Contains(Marker))
                {
                    _logger.LogInformation("[Announcements] index.html already patched at {Path}", full);
                    return;
                }

                // Back up once
                var bak = full + ".bak";
                if (!File.Exists(bak)) File.Copy(full, bak);

                content = content.Replace("</body>", ScriptTag + "\n</body>", StringComparison.OrdinalIgnoreCase);
                File.WriteAllText(full, content, System.Text.Encoding.UTF8);
                _logger.LogInformation("[Announcements] Patched {Path} — banners will auto-load on every page.", full);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Announcements] Could not patch index.html at {Path}", indexPath);
            }
        }

        _logger.LogWarning("[Announcements] jellyfin-web/index.html not found — run patch-jellyfin-web.ps1 as Administrator.");
    }

    private void RegisterWithJsInjector()
    {
        try
        {
            var jsInjectorAssembly = AssemblyLoadContext.All
                .SelectMany(ctx => ctx.Assemblies)
                .FirstOrDefault(a => a.FullName?.Contains("Jellyfin.Plugin.JavaScriptInjector") ?? false);

            if (jsInjectorAssembly is null)
            {
                _logger.LogInformation("[Announcements] JS Injector plugin not found; banner auto-injection disabled.");
                return;
            }

            var pluginInterfaceType = jsInjectorAssembly.GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
            if (pluginInterfaceType is null)
            {
                _logger.LogWarning("[Announcements] JS Injector PluginInterface type not found.");
                return;
            }

            var resourceName = "Jellyfin.Plugin.Announcements.Web.announcement.js";
            string scriptContent;
            using (var stream = GetType().Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream is null)
                {
                    _logger.LogWarning("[Announcements] Embedded resource '{Resource}' not found.", resourceName);
                    return;
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
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            if (_retryCount >= MaxRetries)
            {
                _logger.LogWarning("[Announcements] JS Injector not ready after {MaxRetries} retries.", MaxRetries);
                return;
            }

            _retryCount++;
            _logger.LogInformation("[Announcements] JS Injector not ready; retrying in 5s ({Attempt}/{MaxRetries}).", _retryCount, MaxRetries);
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                RegisterWithJsInjector();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Announcements] Failed to register script with JS Injector.");
        }
    }
}
