namespace FixletBuilder.Core;

public interface IInstalledAppsScanner
{
    List<InstalledApp> Scan();
    Task<List<InstalledApp>> ScanAsync(IProgress<int>? progress = null, CancellationToken ct = default);
}

public interface IWingetIntegration
{
    bool IsAvailable();
    List<WingetPackage> Search(string query);
    WingetPackageDetails? GetDetails(string packageId);
    List<WingetPackage> ListUpgrades();
    Task<List<WingetPackage>> SearchAsync(string query, CancellationToken ct = default);
    Task<WingetPackageDetails?> GetDetailsAsync(string packageId, CancellationToken ct = default);
    Task<List<WingetPackage>> ListUpgradesAsync(CancellationToken ct = default);
}

public interface IAppVersionMonitor
{
    MonitorConfig Config { get; }
    void TrackApp(string appName);
    void UntrackApp(string appName);
    Task<List<VersionCheckResult>> CheckAllAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    List<FixletModel> GenerateUpgradeFixlets(List<VersionCheckResult> results);
    string ExportResultsCsv(List<VersionCheckResult> results);
}

public interface IFixletWriter
{
    void WriteBes(string filePath, FixletModel model);
    void WriteJson(string filePath, FixletModel model);
    string GenerateXml(FixletModel model);
}

public interface IDownloadService
{
    Task<FileHashInfo?> GetFileHashInfoAsync(string url, CancellationToken ct = default);
    FileHashInfo ComputeHashes(byte[] fileData);
    string ComputePrefetchSha1(byte[] fileData);
}