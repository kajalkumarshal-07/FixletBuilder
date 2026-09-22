using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using Polly.Retry;

namespace FixletBuilder.Core;

public class BigFixConsoleOptions
{
    public string BaseUrl { get; set; } = "https://localhost:52311";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? ClientCertificate { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    public bool SkipCertificateValidation { get; set; } = false;
}

public class ImportResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public long? FixletId { get; set; }
    public int HttpStatus { get; set; }
}

public interface IBigFixConsoleClient
{
    Task<bool> PingAsync(CancellationToken ct = default);
    Task<ImportResult> ImportFixletAsync(string filePath, string? siteName = null, CancellationToken ct = default);
}

public sealed class BigFixConsoleClient : IBigFixConsoleClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<BigFixConsoleClient> _logger;
    private readonly BigFixConsoleOptions _options;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    public BigFixConsoleClient(BigFixConsoleOptions options, ILogger<BigFixConsoleClient> logger)
    {
        _options = options;
        _logger = logger;

        var handler = new HttpClientHandler();
        if (options.SkipCertificateValidation)
        {
            // SECURITY WARNING: Disabling TLS certificate validation allows MITM attacks.
            // Credentials and data can be intercepted. NEVER use in production.
            // Only enable for local development/testing with self-signed certificates.
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            _logger.LogWarning("TLS certificate validation is disabled. Do NOT use this in production.");
        }

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };

        if (!string.IsNullOrEmpty(options.Username))
        {
            var creds = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
        }

        _retryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrTransientHttpError()
            .WaitAndRetryAsync(options.MaxRetries,
                attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (outcome, delay, attempt, _) =>
                    _logger.LogWarning(outcome.Exception,
                        "BigFix API retry {Attempt} after {Delay}s: {Message}",
                        attempt, delay.TotalSeconds, outcome.Exception?.Message));
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _retryPolicy.ExecuteAsync(
                ct => _http.GetAsync("api/client/login", ct), ct);
            _logger.LogInformation("BigFix ping: {Status}", resp.StatusCode);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BigFix console unreachable at {Url}", _options.BaseUrl);
            return false;
        }
    }

    public async Task<ImportResult> ImportFixletAsync(string filePath, string? siteName = null, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return new ImportResult { Success = false, Message = $"File not found: {filePath}" };

        try
        {
            await using var fs = File.OpenRead(filePath);
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(fs);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(streamContent, "file", Path.GetFileName(filePath));

            var endpoint = string.IsNullOrEmpty(siteName)
                ? "api/fixlets/import"
                : $"api/fixlets/import?site={Uri.EscapeDataString(siteName)}";

            using var resp = await _retryPolicy.ExecuteAsync(
                ct => _http.PostAsync(endpoint, content, ct), ct);

            var body = await resp.Content.ReadAsStringAsync(ct);
            return new ImportResult
            {
                Success = resp.IsSuccessStatusCode,
                HttpStatus = (int)resp.StatusCode,
                Message = resp.IsSuccessStatusCode ? body : $"HTTP {(int)resp.StatusCode}: {body}",
                FixletId = TryParseFixletId(body)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import fixlet {Path}", filePath);
            return new ImportResult { Success = false, Message = ex.Message };
        }
    }

    private static long? TryParseFixletId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("ID", out var id) ||
                doc.RootElement.TryGetProperty("id", out id) ||
                doc.RootElement.TryGetProperty("FixletID", out id))
            {
                return id.GetInt64();
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    public void Dispose()
    {
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}