using System.IO;
using Microsoft.Win32;

namespace FixletBuilder.Core;

public class InstalledApp
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public string UninstallString { get; set; } = "";
    public string QuietUninstallString { get; set; } = "";
    public string DisplayIcon { get; set; } = "";
    public long? EstimatedSize { get; set; }
    public string Source { get; set; } = "registry";
    public string WingetId { get; set; } = "";
    public string RegistryKeyPath { get; set; } = "";
    public string RegistryValueName { get; set; } = "DisplayVersion";
    public string RegistryValue { get; set; } = "";
    /// <summary>MSI product code ({GUID}) when the Uninstall key is an MSI product key.</summary>
    public string MsiProductCode { get; set; } = "";
}

public sealed class InstalledAppsScanner : IInstalledAppsScanner
{
    private static readonly string[] RegistryKeys = new[]
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    public List<InstalledApp> Scan() => ScanAsync().GetAwaiter().GetResult();

    public async Task<List<InstalledApp>> ScanAsync(IProgress<int>? progress = null, CancellationToken ct = default,
        bool enrichWithWinget = true)
    {
        return await Task.Run(() =>
        {
            var enrichedDict = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
            int totalSteps = RegistryKeys.Length * 2 + 1;
            int step = 0;

            foreach (var keyPath in RegistryKeys)
            {
                ct.ThrowIfCancellationRequested();
                ScanKey(Registry.LocalMachine, keyPath, enrichedDict);
                progress?.Report(++step * 100 / totalSteps);
                ScanKey(Registry.CurrentUser, keyPath, enrichedDict);
                progress?.Report(++step * 100 / totalSteps);
            }

            if (enrichWithWinget)
            {
                EnrichWithWinget(enrichedDict, ct);
                progress?.Report(++step * 100 / totalSteps);
            }

            return enrichedDict.Values
                .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, ct);
    }

    private static void ScanKey(RegistryKey root, string keyPath, Dictionary<string, InstalledApp> apps)
    {
        try
        {
            using var key = root.OpenSubKey(keyPath);
            if (key is null) return;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey is null) continue;

                    var name = subKey.GetValue("DisplayName") as string ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var version = subKey.GetValue("DisplayVersion") as string ?? "";
                    var systemComponent = subKey.GetValue("SystemComponent");
                    var parentName = subKey.GetValue("ParentDisplayName") as string;

                    if (systemComponent is int sc && sc == 1) continue;
                    if (!string.IsNullOrWhiteSpace(parentName)) continue;

                    var dictKey = $"{name}|{version}";
                    if (apps.ContainsKey(dictKey)) continue;

                    var rawIcon = subKey.GetValue("DisplayIcon") as string ?? "";
                    var regVersion = subKey.GetValue("DisplayVersion") as string ?? "";
                    apps[dictKey] = new InstalledApp
                    {
                        Name = name,
                        Version = version,
                        Publisher = subKey.GetValue("Publisher") as string ?? "",
                        InstallLocation = subKey.GetValue("InstallLocation") as string ?? "",
                        UninstallString = subKey.GetValue("UninstallString") as string ?? "",
                        QuietUninstallString = subKey.GetValue("QuietUninstallString") as string ?? "",
                        DisplayIcon = rawIcon,
                        EstimatedSize = (subKey.GetValue("EstimatedSize") as int?) * 1024L,
                        Source = root == Registry.LocalMachine ? "HKLM" : "HKCU",
                        RegistryKeyPath = $@"{root.Name.Replace("HKEY_LOCAL_MACHINE", "HKLM").Replace("HKEY_CURRENT_USER", "HKCU")}\{keyPath}\{subKeyName}",
                        RegistryValueName = "DisplayVersion",
                        RegistryValue = regVersion,
                        MsiProductCode = LooksLikeProductCode(subKeyName) ? subKeyName : ""
                    };
                }
                catch
                {
                    // Skip entries we can't read
                }
            }
        }
        catch
        {
            // Skip keys we can't open
        }
    }

    /// <summary>Uninstall subkey names that are MSI product codes look like {GUID}.</summary>
    private static bool LooksLikeProductCode(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName) || keyName.Length != 38) return false;
        if (keyName[0] != '{' || keyName[^1] != '}') return false;
        return System.Guid.TryParse(keyName, out _);
    }

    private static void EnrichWithWinget(Dictionary<string, InstalledApp> apps, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "list --disable-interactivity --accept-source-agreements",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(30000);

            var lines = output.Split('\n');
            if (lines.Length < 3) return;

            for (int i = 2; i < lines.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var line = lines[i].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("---")) continue;

                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length < 3) continue;

                var wingetId = parts[0];
                var version = parts.Length >= 2 ? parts[^1] : "";

                foreach (var app in apps.Values)
                {
                    if (app.WingetId.Length > 0) continue;

                    if (app.Name.Contains(wingetId, StringComparison.OrdinalIgnoreCase) ||
                        wingetId.Contains(app.Name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        app.WingetId = wingetId;
                        if (string.IsNullOrWhiteSpace(app.Version) && version.Length > 0)
                            app.Version = version;
                    }
                }
            }
        }
        catch
        {
            // winget not available — skip enrichment
        }
    }
}