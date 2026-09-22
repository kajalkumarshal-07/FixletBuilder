using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FixletBuilder.Core;

public class WingetPackage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string AvailableVersion { get; set; } = "";
    public string Source { get; set; } = "";
    public string Publisher { get; set; } = "";
}

public class WingetPackageDetails
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Description { get; set; } = "";
    public string InstallerUrl { get; set; } = "";
    public string InstallerType { get; set; } = "";
    public string SilentArgs { get; set; } = "";
    public string UninstallArgs { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string Sha1 { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long? FileSizeBytes { get; set; }
}

public sealed class WingetIntegration : IWingetIntegration
{
    private readonly ILogger<WingetIntegration> _logger;
    private readonly TimeSpan _commandTimeout;

    public WingetIntegration(ILogger<WingetIntegration> logger, TimeSpan? commandTimeout = null)
    {
        _logger = logger;
        _commandTimeout = commandTimeout ?? TimeSpan.FromSeconds(30);
    }

    public bool IsAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            return proc.WaitForExit((int)_commandTimeout.TotalMilliseconds) && proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "winget not available");
            return false;
        }
    }

    public List<WingetPackage> Search(string query) =>
        RunWinget($"search \"{Escape(query)}\" --disable-interactivity --accept-source-agreements --source winget",
            ParsePackageList, TimeSpan.FromSeconds(30));

    public WingetPackageDetails? GetDetails(string packageId) =>
        ParseDetails(RunWingetRaw($"show \"{Escape(packageId)}\" --disable-interactivity --accept-source-agreements",
            TimeSpan.FromSeconds(30)), packageId);

    public List<WingetPackage> ListUpgrades() =>
        RunWinget("list --upgrade-available --disable-interactivity --accept-source-agreements",
            ParseUpgradeList, TimeSpan.FromSeconds(60));

    public Task<List<WingetPackage>> SearchAsync(string query, CancellationToken ct = default) =>
        Task.Run(() => Search(query), ct);

    public Task<WingetPackageDetails?> GetDetailsAsync(string packageId, CancellationToken ct = default) =>
        Task.Run(() => GetDetails(packageId), ct);

    public Task<List<WingetPackage>> ListUpgradesAsync(CancellationToken ct = default) =>
        Task.Run(() => ListUpgrades(), ct);

    private List<WingetPackage> RunWinget(string args, Func<string, List<WingetPackage>> parser, TimeSpan timeout)
    {
        var raw = RunWingetRaw(args, timeout);
        return parser(raw);
    }

    private string RunWingetRaw(string args, TimeSpan timeout)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null) return "";

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit((int)timeout.TotalMilliseconds);
            return output;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "winget command failed: {Args}", args);
            return "";
        }
    }

    private static List<WingetPackage> ParsePackageList(string output)
    {
        var results = new List<WingetPackage>();
        var lines = output.Split('\n');
        bool headerFound = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Contains("---"))
            {
                headerFound = true;
                continue;
            }
            if (!headerFound || string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 3) continue;

            results.Add(new WingetPackage
            {
                Id = parts[0],
                Name = parts.Length >= 2 ? parts[1] : parts[0],
                Version = parts.Length >= 3 ? parts[2] : "",
                Source = "winget"
            });
        }
        return results;
    }

    private static List<WingetPackage> ParseUpgradeList(string output)
    {
        var results = new List<WingetPackage>();
        var lines = output.Split('\n');
        bool headerFound = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Contains("---"))
            {
                headerFound = true;
                continue;
            }
            if (!headerFound || string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 4) continue;

            results.Add(new WingetPackage
            {
                Id = parts[0],
                Name = parts.Length >= 2 ? parts[1] : parts[0],
                Version = parts.Length >= 3 ? parts[^2] : "",
                AvailableVersion = parts[^1],
                Source = "winget"
            });
        }
        return results;
    }

    private static WingetPackageDetails? ParseDetails(string output, string packageId)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        var details = new WingetPackageDetails { Id = packageId };
        var lines = output.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r').Trim();
            if (trimmed.Contains(':'))
            {
                var colonIdx = trimmed.IndexOf(':');
                var key = trimmed[..colonIdx].Trim().ToLowerInvariant();
                var value = trimmed[(colonIdx + 1)..].Trim();

                switch (key)
                {
                    case "name": details.Name = value; break;
                    case "version": details.Version = value; break;
                    case "publisher": details.Publisher = value; break;
                    case "description": details.Description = value; break;
                    case "installer url": details.InstallerUrl = value; break;
                    case "installer type": details.InstallerType = value; break;
                    case "silent switch": details.SilentArgs = value; break;
                    case "silent uninstall switch": details.UninstallArgs = value; break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(details.SilentArgs))
            details.SilentArgs = SilentArgsDatabase.GetForPackage(packageId);

        return details;
    }

    private static string Escape(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return input
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("'", "\\'")
            .Replace("`", "\\`")
            .Replace("$", "\\$")
            .Replace("!", "\\!");
    }
}