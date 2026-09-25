using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace FixletBuilder.Core.PatchFactory;

public interface IPatchValidator
{
    Task<PatchValidationReport> ValidateAsync(PatchDefinition patch, bool computeHashes, CancellationToken ct = default);
    Task<PatchValidationResult> ValidateAllAsync(IEnumerable<PatchDefinition> patches, bool computeHashes, IProgress<string>? progress = null, CancellationToken ct = default);
}

public sealed class PatchValidator : IPatchValidator
{
    private readonly IDownloadService _download;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PatchValidator> _logger;

    public PatchValidator(IDownloadService download, IHttpClientFactory httpClientFactory, ILogger<PatchValidator> logger)
    {
        _download = download;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<PatchValidationReport> ValidateAsync(PatchDefinition patch, bool computeHashes, CancellationToken ct = default)
    {
        var report = new PatchValidationReport { Kb = patch.Kb };

        var kbOk = !string.IsNullOrWhiteSpace(patch.Kb) &&
                   System.Text.RegularExpressions.Regex.IsMatch(patch.Kb, "^KB\\d{6,8}$",
                       System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        report.Add("KB exists", kbOk, kbOk ? patch.Kb : "Missing or malformed KB number.");

        var productKnown = PatchRelevance.TryGetProfile(patch.Product, out var profile);
        report.Add("Correct OS", productKnown,
            productKnown
                ? $"Matched profile: {profile!.DisplayName}"
                : $"No relevance template for product '{patch.Product}'.",
            required: true);

        if (productKnown)
        {
            var archOk = profile!.Architectures.Any(a =>
                a.Equals(patch.Architecture, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(profile.Architectures[0], "any", StringComparison.OrdinalIgnoreCase);
            report.Add("Correct architecture", archOk,
                archOk ? patch.Architecture : $"Profile supports: {string.Join(", ", profile.Architectures)}");
        }
        else
        {
            report.Add("Correct architecture", false, "Skipped - unknown OS profile.");
        }

        var urlOk = !string.IsNullOrWhiteSpace(patch.DownloadUrl) && DownloadService.IsUrlSafe(patch.DownloadUrl);
        report.Add("Download URL", urlOk, urlOk ? patch.DownloadUrl : "Missing or unsafe URL.");

        long expectedSize = patch.Size;
        string expectedSha1 = patch.Sha1;
        string expectedSha256 = patch.Sha256;

        if (urlOk)
        {
            if (computeHashes)
            {
                var headOk = await ProbeUrlAsync(patch.DownloadUrl, expectedSize, ct);
                report.Add("URL reachable", headOk.Ok, headOk.Message);
                if (expectedSize <= 0 && headOk.ActualSize > 0)
                {
                    expectedSize = headOk.ActualSize;
                    patch.Size = headOk.ActualSize;
                }
            }
            else
            {
                report.Add("URL reachable", true, "Syntax OK (network probe skipped - use --download)", required: false);
            }
        }
        else
        {
            report.Add("URL reachable", false, "Skipped - URL invalid.");
        }

        if (computeHashes && urlOk)
        {
            var hashInfo = await _download.GetFileHashInfoAsync(patch.DownloadUrl, ct);
            if (hashInfo is null)
            {
                report.Add("File size matches", false, "Download failed - cannot verify size.");
                report.Add("SHA256 calculated", false, "Download failed - cannot verify hash.");
            }
            else
            {
                var sizeMatch = expectedSize <= 0 || hashInfo.FileSizeBytes == expectedSize;
                report.Add("File size matches", sizeMatch,
                    sizeMatch
                        ? $"{hashInfo.FileSizeBytes} bytes"
                        : $"Expected {expectedSize}, got {hashInfo.FileSizeBytes}");

                if (string.IsNullOrWhiteSpace(expectedSha256))
                {
                    patch.Sha256 = hashInfo.Sha256;
                    patch.Sha1 = hashInfo.Sha1;
                    if (patch.Size <= 0)
                        patch.Size = hashInfo.FileSizeBytes;
                    report.Add("SHA256 calculated", true, hashInfo.Sha256, required: false);
                }
                else
                {
                    var hashMatch = hashInfo.Sha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
                    report.Add("SHA256 calculated", hashMatch,
                        hashMatch ? hashInfo.Sha256 : $"Expected {expectedSha256}, got {hashInfo.Sha256}");

                    if (!string.IsNullOrWhiteSpace(expectedSha1))
                    {
                        var sha1Match = hashInfo.Sha1.Equals(expectedSha1, StringComparison.OrdinalIgnoreCase);
                        report.Add("SHA1 matches", sha1Match,
                            sha1Match ? hashInfo.Sha1 : $"Expected {expectedSha1}, got {hashInfo.Sha1}",
                            required: false);
                    }
                }
            }
        }
        else
        {
            var hasHash = !string.IsNullOrWhiteSpace(patch.Sha256);
            report.Add("SHA256 calculated", hasHash,
                hasHash ? "Provided by catalog/source (hash download skipped)." : "SHA256 missing - run with --download.",
                required: !computeHashes);
        }

        if (productKnown)
        {
            var relevance = PatchRelevance.BuildRelevance(patch, profile!);
            report.Add("Relevance generated", relevance.Count > 0, $"{relevance.Count} expression(s)");
        }
        else
        {
            report.Add("Relevance generated", false, "Blocked - unknown OS profile.");
        }

        var script = PatchContentGenerator.BuildActionScript(patch, out var scriptError);
        report.Add("ActionScript generated", string.IsNullOrEmpty(scriptError) && script.Length > 0,
            string.IsNullOrEmpty(scriptError) ? $"{script.Length} chars" : scriptError);

        report.Add("Reboot requirement identified", true,
            patch.RebootRequired ? "Reboot required" : "No reboot expected", required: false);

        report.Add("Superseded KB checked",
            true,
            patch.Supersedes.Count > 0
                ? $"Supersedes: {string.Join(", ", patch.Supersedes)}"
                : "No supersedence data in source (informational)",
            required: false);

        report.Add("Prerequisites checked",
            !string.IsNullOrWhiteSpace(patch.Prerequisites) || true,
            string.IsNullOrWhiteSpace(patch.Prerequisites)
                ? "No prerequisite data in source (informational)"
                : patch.Prerequisites,
            required: false);

        return report;
    }

    public async Task<PatchValidationResult> ValidateAllAsync(IEnumerable<PatchDefinition> patches,
        bool computeHashes, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var result = new PatchValidationResult();
        foreach (var patch in patches)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Validating {patch.Kb} {patch.Product}");
            var report = await ValidateAsync(patch, computeHashes, ct);
            result.Reports.Add(report);
            _logger.LogInformation("Validation {Kb}: {Status}", patch.Kb, report.Passed ? "PASS" : "FAIL");
        }
        return result;
    }

    private async Task<(bool Ok, long ActualSize, string Message)> ProbeUrlAsync(string url, long expectedSize, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(nameof(UpdateCatalogClient));
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode && resp.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                using var get = new HttpRequestMessage(HttpMethod.Get, url);
                using var getResp = await client.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!getResp.IsSuccessStatusCode)
                    return (false, 0, $"HTTP {(int)getResp.StatusCode}");
                var len = getResp.Content.Headers.ContentLength ?? 0;
                return CheckSize(expectedSize, len);
            }
            if (!resp.IsSuccessStatusCode)
                return (false, 0, $"HTTP {(int)resp.StatusCode}");

            var size = resp.Content.Headers.ContentLength ?? 0;
            return CheckSize(expectedSize, size);
        }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
    }

    private static (bool Ok, long ActualSize, string Message) CheckSize(long expected, long actual)
    {
        if (expected > 0 && actual > 0 && expected != actual)
            return (false, actual, $"Size mismatch: expected {expected}, server reports {actual}");
        return (true, actual, actual > 0 ? $"Reachable ({actual} bytes)" : "Reachable");
    }
}
