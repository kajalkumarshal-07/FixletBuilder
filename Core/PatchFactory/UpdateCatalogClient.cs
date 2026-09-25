using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace FixletBuilder.Core.PatchFactory;

public interface IUpdateCatalogClient
{
    Task<List<CatalogSearchHit>> SearchAsync(string query, CancellationToken ct = default);
    Task<List<PatchFileCandidate>> GetDownloadFilesAsync(string updateId, CancellationToken ct = default);
    Task<List<PatchDefinition>> DiscoverAsync(DiscoverRequest request, IProgress<string>? progress = null, CancellationToken ct = default);
}

public class DiscoverRequest
{
    public List<string> KbNumbers { get; set; } = new();
    public string? Query { get; set; }
    public string? ProductFilter { get; set; }
    public DateTime? ReleasedAfter { get; set; }
    public bool PatchTuesday { get; set; }
    public bool ScanWindows10 { get; set; }
    public bool ScanWindows11 { get; set; }
    public bool ScanThisMonth { get; set; }
    public string Architecture { get; set; } = "x64";
    public int MaxResults { get; set; } = 40;

    public bool WantsMonthScan => ScanThisMonth || PatchTuesday || ScanWindows10 || ScanWindows11;
}

public class CatalogSearchHit
{
    public string UpdateId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Products { get; set; } = "";
    public string Classification { get; set; } = "";
    public DateTime? LastUpdated { get; set; }
}

public class PatchFileCandidate
{
    public string Url { get; set; } = "";
    public long Size { get; set; }
    public string Sha1 { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Architectures { get; set; } = "";
    public string FileName { get; set; } = "";
}

public sealed class UpdateCatalogClient : IUpdateCatalogClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<UpdateCatalogClient> _logger;

    private const string CatalogBase = "https://www.catalog.update.microsoft.com";

    public UpdateCatalogClient(ILogger<UpdateCatalogClient> logger, HttpClient httpClient)
    {
        _logger = logger;
        _http = httpClient;
        _http.Timeout = TimeSpan.FromSeconds(60);
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) FixletBuilder/1.0");
    }

    public static DateTime LastPatchTuesday(DateTime? from = null)
    {
        var anchor = (from ?? DateTime.Today).Date;
        var first = new DateTime(anchor.Year, anchor.Month, 1);
        var tuesday = first.AddDays(((int)DayOfWeek.Tuesday - (int)first.DayOfWeek + 7) % 7);
        if (tuesday > anchor)
            tuesday = tuesday.AddMonths(-1);
        var second = tuesday.AddDays(7);
        return second <= anchor ? second : tuesday;
    }

    public async Task<List<CatalogSearchHit>> SearchAsync(string query, CancellationToken ct = default)
    {
        var hits = new List<CatalogSearchHit>();
        if (string.IsNullOrWhiteSpace(query))
            return hits;

        var url = $"{CatalogBase}/Search.aspx?q={Uri.EscapeDataString(query)}";
        _logger.LogInformation("Catalog search: {Query}", query);

        var html = await _http.GetStringAsync(url, ct);

        // Result cells: id="{guid}_C{n}_R{row}" with goToDetails("{guid}")
        var cellIds = Regex.Matches(html,
            @"id\s*=\s*['""]([0-9a-fA-F\-]{36})_C0_R\d+['""]",
            RegexOptions.IgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match idMatch in cellIds)
        {
            var id = idMatch.Groups[1].Value;
            if (!seen.Add(id))
                continue;

            // Title cell (C1), Product (C2), Classification (C3), Last Updated (C4), Size (C6)
            var titleM = Regex.Match(html,
                $@"id\s*=\s*['""]{Regex.Escape(id)}_C1_R\d+['""][^>]*>\s*<a[^>]*>([\s\S]*?)</a>",
                RegexOptions.IgnoreCase);
            var productM = Regex.Match(html,
                $@"id\s*=\s*['""]{Regex.Escape(id)}_C2_R\d+['""][^>]*>\s*([\s\S]*?)</td>",
                RegexOptions.IgnoreCase);
            var classM = Regex.Match(html,
                $@"id\s*=\s*['""]{Regex.Escape(id)}_C3_R\d+['""][^>]*>\s*([\s\S]*?)</td>",
                RegexOptions.IgnoreCase);
            var dateM = Regex.Match(html,
                $@"id\s*=\s*['""]{Regex.Escape(id)}_C4_R\d+['""][^>]*>\s*([\s\S]*?)</td>",
                RegexOptions.IgnoreCase);

            var title = CleanCell(titleM.Success ? titleM.Groups[1].Value : "");
            if (title.Length == 0)
            {
                // Fallback: anchor text near goToDetails(id)
                var near = Regex.Match(html,
                    $@"goToDetails\(\s*['""]{Regex.Escape(id)}['""]\s*\)[^>]*>\s*([\s\S]*?)</a>",
                    RegexOptions.IgnoreCase);
                title = CleanCell(near.Groups[1].Value);
            }

            if (title.Length == 0)
                continue;

            DateTime? lastUpdated = null;
            if (dateM.Success &&
                DateTime.TryParse(CleanCell(dateM.Groups[1].Value),
                    System.Globalization.CultureInfo.GetCultureInfo("en-US"),
                    System.Globalization.DateTimeStyles.None, out var dt))
            {
                lastUpdated = dt;
            }

            hits.Add(new CatalogSearchHit
            {
                UpdateId = id,
                Title = title,
                Products = CleanCell(productM.Success ? productM.Groups[1].Value : ""),
                Classification = CleanCell(classM.Success ? classM.Groups[1].Value : ""),
                LastUpdated = lastUpdated
            });
        }

        if (hits.Count == 0)
        {
            // Fallback: goToDetails blocks with title text following the anchor
            foreach (Match m in Regex.Matches(html,
                         @"goToDetails\(\s*['""]([0-9a-fA-F\-]{36})['""]\s*\)[^>]*>\s*([^<]{5,300})",
                         RegexOptions.IgnoreCase))
            {
                if (!seen.Add(m.Groups[1].Value))
                    continue;
                var title = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
                if (title.Length < 5)
                    continue;
                hits.Add(new CatalogSearchHit
                {
                    UpdateId = m.Groups[1].Value,
                    Title = title,
                    Products = "",
                    Classification = title.Contains("Security", StringComparison.OrdinalIgnoreCase)
                        ? "Security Updates"
                        : "Updates",
                    LastUpdated = null
                });
            }
        }

        _logger.LogInformation("Catalog search returned {Count} hit(s)", hits.Count);
        return hits;
    }

    private static string CleanCell(string html)
    {
        var text = Regex.Replace(html ?? "", "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    public async Task<List<PatchFileCandidate>> GetDownloadFilesAsync(string updateId, CancellationToken ct = default)
    {
        var files = new List<PatchFileCandidate>();
        if (string.IsNullOrWhiteSpace(updateId))
            return files;

        // Catalog expects JSON array form field (not a bare GUID)
        var payloadJson = "[{\"size\":0,\"updateID\":\"" + updateId + "\",\"uidInfo\":\"" + updateId + "\"}]";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["updateIDs"] = payloadJson
        });

        var resp = await _http.PostAsync($"{CatalogBase}/DownloadDialog.aspx", content, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct);

        // Patterns: downloadInformation[i].files[j].url = '...'
        //           downloadInformation[i].files[j].sha1 = '...'
        //           or [i].url / [i].sha1 style
        var urls = Regex.Matches(body, @"(?:files\[\d+\]|\[\d+\])\.url\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
        var sizes = Regex.Matches(body, @"(?:files\[\d+\]|\[\d+\])\.size\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
        var sha1s = Regex.Matches(body, @"(?:files\[\d+\]|\[\d+\])\.sha1\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
        var sha256s = Regex.Matches(body, @"(?:files\[\d+\]|\[\d+\])\.sha256\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
        var archs = Regex.Matches(body, @"(?:files\[\d+\]|\[\d+\])\.architectures\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
        var names = Regex.Matches(body, @"(?:files\[\d+\]|\[\d+\])\.fileName\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);

        // Fallback: any absolute HTTP(S) URL pointing at Windows Update delivery hosts
        if (urls.Count == 0)
        {
            urls = Regex.Matches(body,
                @"https?://(?:w{0,3}\.)?(?:dl\.delivery\.mp\.microsoft\.com|download\.windowsupdate\.com|catalog\.s\.download\.windowsupdate\.com)/[^'"">\s]+",
                RegexOptions.IgnoreCase);
        }

        for (int i = 0; i < urls.Count; i++)
        {
            var url = System.Net.WebUtility.HtmlDecode(urls[i].Groups[1].Value).Trim();
            if (url.Length == 0)
                continue;

            long size = 0;
            if (i < sizes.Count)
                long.TryParse(Regex.Replace(sizes[i].Groups[1].Value, "[^0-9]", ""), out size);

            var fileName = i < names.Count && names[i].Groups[1].Value.Length > 0
                ? names[i].Groups[1].Value.Trim()
                : ExtractFileName(url);

            files.Add(new PatchFileCandidate
            {
                Url = url,
                Size = size,
                Sha1 = i < sha1s.Count ? NormalizeHash(sha1s[i].Groups[1].Value) : "",
                Sha256 = i < sha256s.Count ? NormalizeHash(sha256s[i].Groups[1].Value) : "",
                Architectures = i < archs.Count ? archs[i].Groups[1].Value : "",
                FileName = fileName
            });
        }

        _logger.LogInformation("Update {UpdateId}: {Count} download file(s)", updateId, files.Count);
        return files;
    }

    private static string NormalizeHash(string raw)
    {
        var h = (raw ?? "").Trim().ToLowerInvariant();
        // Catalog may expose Base64; convert to hex for comparison with Get-FileHash
        if (h.Length > 0 && !Regex.IsMatch(h, "^[0-9a-f]+$"))
        {
            try
            {
                var bytes = Convert.FromBase64String(raw.Trim());
                h = Convert.ToHexString(bytes).ToLowerInvariant();
            }
            catch
            {
                // leave as-is
            }
        }
        return h;
    }

    public async Task<List<PatchDefinition>> DiscoverAsync(DiscoverRequest request,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var queries = BuildQueries(request);
        var seenUpdateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hits = new List<CatalogSearchHit>();

        foreach (var query in queries)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Searching catalog: {query}");
            var found = await SearchAsync(query, ct);
            foreach (var hit in found)
            {
                if (seenUpdateIds.Add(hit.UpdateId))
                    hits.Add(hit);
            }
        }

        var releasedAfter = request.ReleasedAfter ?? InferReleaseFloor(request);
        if (releasedAfter is { } after)
            hits = hits.Where(h => h.LastUpdated is null || h.LastUpdated >= after.Date).ToList();

        if (!string.IsNullOrWhiteSpace(request.ProductFilter))
            hits = hits.Where(h =>
                h.Title.Contains(request.ProductFilter, StringComparison.OrdinalIgnoreCase) ||
                h.Products.Contains(request.ProductFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        if (request.WantsMonthScan || request.ScanWindows10 || request.ScanWindows11)
            hits = hits.Where(h => IsWindowsClientUpdate(h)).ToList();

        hits = hits
            .Where(h => IsSecurityOrUpdateClassification(h.Classification))
            .OrderByDescending(h => h.LastUpdated ?? DateTime.MinValue)
            .Take(request.MaxResults)
            .ToList();

        progress?.Report($"Matched {hits.Count} catalog update(s) after filters");

        var patches = new List<PatchDefinition>();
        foreach (var hit in hits)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Resolving downloads: {hit.Title}");

            List<PatchFileCandidate> files;
            try
            {
                files = await GetDownloadFilesAsync(hit.UpdateId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Download dialog failed for {UpdateId}", hit.UpdateId);
                continue;
            }

            var file = PickFile(files, request.Architecture);
            if (file is null)
            {
                _logger.LogWarning("No matching {Arch} file for {Title}", request.Architecture, hit.Title);
                continue;
            }

            var kb = ExtractKb(hit.Title);
            patches.Add(new PatchDefinition
            {
                Kb = kb,
                Title = hit.Title,
                Product = hit.Products,
                Classification = hit.Classification,
                ReleaseDate = hit.LastUpdated,
                UpdateId = hit.UpdateId,
                Architecture = request.Architecture,
                DownloadUrl = file.Url,
                InstallerFileName = file.FileName,
                Size = file.Size,
                Sha1 = file.Sha1,
                Sha256 = file.Sha256,
                Cves = ExtractCves(hit.Title),
                RebootRequired = !hit.Title.Contains("Update for Microsoft Edge", StringComparison.OrdinalIgnoreCase)
            });
        }

        progress?.Report($"Discovered {patches.Count} patch definition(s)");
        return patches;
    }

    private static DateTime? InferReleaseFloor(DiscoverRequest request)
    {
        if (request.WantsMonthScan || request.PatchTuesday || request.ScanThisMonth ||
            request.ScanWindows10 || request.ScanWindows11)
        {
            var anchor = LastPatchTuesday(DateTime.Today);
            return anchor.AddDays(-1);
        }
        return null;
    }

    private static bool IsWindowsClientUpdate(CatalogSearchHit hit)
    {
        var blob = $"{hit.Title} {hit.Products}".ToLowerInvariant();

        var isWin10 = blob.Contains("windows 10") ||
                      blob.Contains("windows10") ||
                      blob.Contains("version 22h2") && blob.Contains("windows");
        var isWin11 = blob.Contains("windows 11") ||
                      blob.Contains("windows11") ||
                      (blob.Contains("version 23h2") || blob.Contains("version 24h2") ||
                       blob.Contains("version 22h2")) && blob.Contains("windows");

        if (isWin10 || isWin11)
            return true;

        // Generic monthly cumulative titles that still apply to client SKUs
        return blob.Contains("cumulative update") &&
               (blob.Contains("windows 10") || blob.Contains("windows 11") ||
                blob.Contains("x64-based") || blob.Contains("for windows"));
    }

    private static List<string> BuildQueries(DiscoverRequest request)
    {
        var queries = new List<string>();

        foreach (var kb in request.KbNumbers)
        {
            var normalized = kb.Trim();
            if (normalized.Length == 0)
                continue;
            if (!normalized.StartsWith("KB", StringComparison.OrdinalIgnoreCase))
                normalized = "KB" + normalized;
            queries.Add(normalized);
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
            queries.Add(request.Query);

        if (request.WantsMonthScan)
        {
            var floor = request.ReleasedAfter ?? LastPatchTuesday(DateTime.Today);

            if (request.ScanWindows10 || request.ScanThisMonth || request.PatchTuesday)
            {
                queries.AddRange(new[]
                {
                    "Windows 10 Version 22H2 for x64-based Systems",
                    "Windows 10 Version 22H2 x64",
                    "Windows 10 22H2 Security Update",
                    "Windows 10 Version 22H2 Cumulative Update"
                });
            }

            if (request.ScanWindows11 || request.ScanThisMonth || request.PatchTuesday)
            {
                queries.AddRange(new[]
                {
                    "Windows 11 Version 24H2 for x64-based Systems",
                    "Windows 11 Version 23H2 for x64-based Systems",
                    "Windows 11 24H2 Cumulative Update",
                    "Windows 11 23H2 Cumulative Update",
                    "Windows 11 Version 24H2 Security Update",
                    "Windows 11 Version 23H2 Security Update"
                });
            }

            // Broader fallbacks that still return current LCU/SSU rows
            queries.Add("Windows 10 Version 22H2 Update");
            queries.Add("Windows 11 Version 24H2 Update");
        }

        if (queries.Count == 0 && !string.IsNullOrWhiteSpace(request.ProductFilter))
            queries.Add(request.ProductFilter);

        if (queries.Count == 0 && request.ScanThisMonth)
        {
            var floor = LastPatchTuesday(DateTime.Today);
            queries.Add($"Security Updates {floor:yyyy-MM}");
            queries.Add($"{floor:MMMM} security updates");
        }

        return queries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsSecurityOrUpdateClassification(string classification)
    {
        if (string.IsNullOrWhiteSpace(classification))
            return true;
        var c = classification.ToLowerInvariant();
        if (c.Contains("defender") || c.Contains("definition") && !c.Contains("security"))
            return false;
        return c.Contains("security") || c.Contains("updates") ||
               c.Contains("critical") || c.Contains("service pack") ||
               c.Contains("cumulative") || c.Contains("startings");
    }

    private static PatchFileCandidate? PickFile(List<PatchFileCandidate> files, string architecture)
    {
        if (files.Count == 0)
            return null;

        var arch = architecture.ToLowerInvariant();
        var scored = files
            .Select(f => new { File = f, Score = ScoreArch(f, arch) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.File.Size)
            .ToList();

        return scored.Count > 0 ? scored[0].File : files.OrderByDescending(f => f.Size).FirstOrDefault();
    }

    private static int ScoreArch(PatchFileCandidate file, string arch)
    {
        var blob = $"{file.Architectures} {file.Url} {file.FileName}".ToLowerInvariant();
        if (blob.Contains("arm64") || blob.Contains("arm64-based"))
            return arch == "arm64" ? 10 : 0;
        if (arch == "x64")
        {
            if (blob.Contains("x64") || blob.Contains("amd64") || blob.Contains("x86_64"))
                return 10;
            if (blob.Contains("x86") || blob.Contains("32-bit"))
                return 1;
            if (file.Architectures.Contains("x64", StringComparison.OrdinalIgnoreCase))
                return 10;
            return 5;
        }
        if (arch == "x86")
        {
            if (blob.Contains("x86") && !blob.Contains("x64"))
                return 10;
            return 2;
        }
        return 5;
    }

    public static string ExtractKb(string title)
    {
        var m = Regex.Match(title, @"\bKB(\d{6,8})\b", RegexOptions.IgnoreCase);
        return m.Success ? "KB" + m.Groups[1].Value : "";
    }

    private static List<string> ExtractCves(string title)
    {
        return Regex.Matches(title, @"CVE-\d{4}-\d{4,7}", RegexOptions.IgnoreCase)
            .Cast<Match>()
            .Select(m => m.Value.ToUpperInvariant())
            .Distinct()
            .ToList();
    }

    private static string ExtractFileName(string url)
    {
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.LocalPath);
            return string.IsNullOrEmpty(name) ? "update.bin" : name;
        }
        catch
        {
            return "update.bin";
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
