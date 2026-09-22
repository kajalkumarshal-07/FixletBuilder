using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace FixletBuilder.Core;

public class FileHashInfo
{
    public string Sha1 { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long FileSizeBytes { get; set; }
}

public sealed class DownloadService : IDownloadService
{
    private readonly ILogger<DownloadService> _logger;
    private readonly HttpClient _httpClient;

    private static readonly string[] BlockedHosts = new[]
    {
        "localhost", "127.0.0.1", "::1",
        "169.254.169.254",  // AWS metadata
        "metadata.google.internal",  // GCP metadata
        "169.254.169.254.nip.io"
    };

    public DownloadService(ILogger<DownloadService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(2);
    }

    public static bool IsUrlSafe(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != "http" && uri.Scheme != "https")
            return false;

        var host = uri.Host;

        if (BlockedHosts.Any(bh => string.Equals(host, bh, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip))
                return false;
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return false;
            if (ip.GetAddressBytes() is [10, ..] || ip.GetAddressBytes() is [172, >= 16, <= 31, ..] || ip.GetAddressBytes() is [192, 168, ..])
                return false;
        }

        return true;
    }

    public async Task<FileHashInfo?> GetFileHashInfoAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (!IsUrlSafe(url))
        {
            _logger.LogWarning("Blocked potentially unsafe URL: {Url}", url);
            return null;
        }

        try
        {
            _logger.LogInformation("Downloading file from {Url} to compute hashes...", url);
            var data = await _httpClient.GetByteArrayAsync(url, ct);

            if (data.Length > 500 * 1024 * 1024)
            {
                _logger.LogWarning("File too large ({Size} bytes), skipping hash computation", data.Length);
                return null;
            }

            var result = ComputeHashes(data);
            _logger.LogInformation("Computed hashes: SHA1={Sha1}, SHA256={Sha256}, Size={Size}",
                result.Sha1, result.Sha256, result.FileSizeBytes);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download or hash file from {Url}", url);
            return null;
        }
    }

    public FileHashInfo ComputeHashes(byte[] fileData)
    {
        using var sha1 = SHA1.Create();
        using var sha256 = SHA256.Create();

        var sha1Hash = sha1.ComputeHash(fileData);
        var sha256Hash = sha256.ComputeHash(fileData);

        return new FileHashInfo
        {
            Sha1 = Convert.ToHexString(sha1Hash).ToLowerInvariant(),
            Sha256 = Convert.ToHexString(sha256Hash).ToLowerInvariant(),
            FileSizeBytes = fileData.Length
        };
    }

    public string ComputePrefetchSha1(byte[] fileData)
    {
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(fileData);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
