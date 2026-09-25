using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FixletBuilder.Core;

public class VersionCheckResult
{
    public string AppName { get; set; } = "";
    public string InstalledVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public bool UpdateAvailable { get; set; }
    public string PackageId { get; set; } = "";
    public string InstallerUrl { get; set; } = "";
    public string Source { get; set; } = "";
}

public class MonitorConfig
{
    public List<string> TrackedApps { get; set; } = new();
    public DateTime LastCheck { get; set; }
    public int CheckIntervalHours { get; set; } = 24;
}

public sealed class AppVersionMonitor : IAppVersionMonitor
{
    private readonly ILogger<AppVersionMonitor> _logger;
    private readonly IWingetIntegration _winget;
    private readonly IInstalledAppsScanner _scanner;

    public MonitorConfig Config { get; private set; } = new();

    private static readonly string DefaultConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FixletBuilder");
    private static readonly string DefaultConfigPath = Path.Combine(DefaultConfigDir, "monitor.json");

    public string ConfigPath { get; }

    public AppVersionMonitor(ILogger<AppVersionMonitor> logger, IWingetIntegration winget, IInstalledAppsScanner scanner)
        : this(logger, winget, scanner, DefaultConfigPath)
    {
    }

    public AppVersionMonitor(ILogger<AppVersionMonitor> logger, IWingetIntegration winget,
        IInstalledAppsScanner scanner, string configPath)
    {
        _logger = logger;
        _winget = winget;
        _scanner = scanner;
        ConfigPath = configPath;
        LoadConfig();
    }

    public void TrackApp(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName)) return;
        if (!Config.TrackedApps.Contains(appName, StringComparer.OrdinalIgnoreCase))
        {
            Config.TrackedApps.Add(appName);
            SaveConfig();
            _logger.LogInformation("Now tracking: {App}", appName);
        }
    }

    public void UntrackApp(string appName)
    {
        var removed = Config.TrackedApps.RemoveAll(a => a.Equals(appName, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            SaveConfig();
            _logger.LogInformation("Stopped tracking: {App}", appName);
        }
    }

    public async Task<List<VersionCheckResult>> CheckAllAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var results = new List<VersionCheckResult>();
        var upgrades = await _winget.ListUpgradesAsync(ct).ConfigureAwait(false);
        var total = Config.TrackedApps.Count;

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var appName = Config.TrackedApps[i];

            var match = upgrades.FirstOrDefault(u =>
                u.Name.Contains(appName, StringComparison.OrdinalIgnoreCase) ||
                appName.Contains(u.Name, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                results.Add(new VersionCheckResult
                {
                    AppName = appName,
                    InstalledVersion = match.Version,
                    LatestVersion = match.AvailableVersion,
                    UpdateAvailable = match.Version != match.AvailableVersion,
                    PackageId = match.Id,
                    Source = "winget"
                });
            }
            else
            {
                var installed = _scanner.Scan()
                    .FirstOrDefault(a => a.Name.Contains(appName, StringComparison.OrdinalIgnoreCase));

                if (installed is not null)
                {
                    results.Add(new VersionCheckResult
                    {
                        AppName = appName,
                        InstalledVersion = installed.Version,
                        LatestVersion = installed.Version,
                        UpdateAvailable = false,
                        Source = "registry"
                    });
                }
                else
                {
                    _logger.LogWarning("Tracked app not found: {App}", appName);
                }
            }

            progress?.Report((int)((i + 1.0) / total * 100));
        }

        Config.LastCheck = DateTime.Now;
        SaveConfig();
        return results;
    }

    public List<FixletModel> GenerateUpgradeFixlets(List<VersionCheckResult> results)
    {
        var fixlets = new List<FixletModel>();

        foreach (var r in results.Where(r => r.UpdateAvailable))
        {
            var details = !string.IsNullOrWhiteSpace(r.PackageId)
                ? _winget.GetDetails(r.PackageId)
                : null;

            var installPath = details?.InstallLocation ?? "";
            var silentArgs = details?.SilentArgs ?? SilentArgsDatabase.GetForApp(r.AppName);
            var sourceUrl = details?.InstallerUrl ?? r.InstallerUrl;

            const string type = FixletTemplates.TypeUpgrade;
            var template = FixletTemplates.Apply(
                type,
                r.AppName,
                r.LatestVersion,
                sourceUrl,
                silentArgs,
                installPath,
                "");

            var detection = RelevanceBuilder.BuildDetection(new DetectionInput
            {
                Type = type,
                AppName = r.AppName,
                Version = r.LatestVersion,
                InstallPath = installPath
            });

            fixlets.Add(new FixletModel
            {
                Title = $"Upgrade {r.AppName} to {r.LatestVersion}",
                Category = "Applications",
                Source = "FixletBuilder AutoMonitor",
                SourceId = r.PackageId,
                Relevance = detection.Relevance,
                Description = template.Description,
                ActionDescription = template.ActionDescription,
                ActionScript = template.ActionScript,
                SuccessCriteria = detection.SuccessCriteria
            });
        }

        return fixlets;
    }

    public string ExportResultsCsv(List<VersionCheckResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("AppName,InstalledVersion,LatestVersion,UpdateAvailable,PackageId,Source");
        foreach (var r in results)
        {
            sb.AppendLine($"\"{Escape(r.AppName)}\",\"{Escape(r.InstalledVersion)}\",\"{Escape(r.LatestVersion)}\",{r.UpdateAvailable},\"{Escape(r.PackageId)}\",\"{Escape(r.Source)}\"");
        }
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\"", "\"\"");

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
                Config = JsonSerializer.Deserialize<MonitorConfig>(File.ReadAllText(ConfigPath)) ?? new MonitorConfig();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load monitor config from {Path}", ConfigPath);
            Config = new MonitorConfig();
        }
    }

    private void SaveConfig()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save monitor config to {Path}", ConfigPath);
        }
    }
}