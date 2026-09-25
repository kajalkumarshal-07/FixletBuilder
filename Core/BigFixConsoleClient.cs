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
    Task<List<RemoteContentItem>> ListContentAsync(string contentType, string siteType, string siteName, CancellationToken ct = default);
    Task<ImportResult> PublishContentXmlAsync(string contentType, string siteType, string siteName, string xml, CancellationToken ct = default);
    Task<ImportResult> CreateAutomaticGroupAsync(string xml, CancellationToken ct = default);
    Task<ImportResult> StartActionAsync(string xml, CancellationToken ct = default);
    Task<ActionStatus?> GetActionStatusAsync(long actionId, CancellationToken ct = default);
    Task<string?> EvaluateRelevanceAsync(string relevance, CancellationToken ct = default);
    Task<ImportResult> ImportAnalysisAsync(string xml, string? siteName = null, CancellationToken ct = default);
}

public class RemoteContentItem
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceId { get; set; } = "";
}

public class ActionStatus
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long? RelevantComputers { get; set; }
    public long? Taken { get; set; }
    public long? Failed { get; set; }
    public long? Pending { get; set; }
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

    public async Task<List<RemoteContentItem>> ListContentAsync(string contentType, string siteType, string siteName, CancellationToken ct = default)
    {
        var items = new List<RemoteContentItem>();
        var endpoint = $"api/{contentType}/{Uri.EscapeDataString(siteType)}/{Uri.EscapeDataString(siteName)}";

        using var resp = await _retryPolicy.ExecuteAsync(
            ct => _http.GetAsync(endpoint, ct), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"List {contentType} failed: HTTP {(int)resp.StatusCode}");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var array = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array ? v : default;

            if (array.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in array.EnumerateArray())
                {
                    items.Add(new RemoteContentItem
                    {
                        Id = ReadInt64(el, "ID", "Id", "id"),
                        Title = ReadString(el, "Title", "Name", "name"),
                        Name = ReadString(el, "Name", "Title", "name"),
                        Source = ReadString(el, "Source", "source"),
                        SourceId = ReadString(el, "SourceID", "SourceId", "sourceID")
                    });
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed parsing content list response");
        }

        return items;
    }

    public async Task<ImportResult> PublishContentXmlAsync(string contentType, string siteType, string siteName, string xml, CancellationToken ct = default)
    {
        var endpoint = $"api/{contentType}/{Uri.EscapeDataString(siteType)}/{Uri.EscapeDataString(siteName)}";
        using var content = new StringContent(xml, Encoding.UTF8, "application/xml");

        using var resp = await _retryPolicy.ExecuteAsync(
            ct => _http.PostAsync(endpoint, content, ct), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        return new ImportResult
        {
            Success = resp.IsSuccessStatusCode,
            HttpStatus = (int)resp.StatusCode,
            Message = resp.IsSuccessStatusCode ? body : $"HTTP {(int)resp.StatusCode}: {body}",
            FixletId = TryParseFixletId(body) ?? TryParseIdFromLocation(resp)
        };
    }

    /// <summary>
    /// POST BES Analysis XML to the BigFix REST API.
    /// Primary: POST api/analyses/{site}  |  Fallback: POST api/analyses (master).
    /// </summary>
    public async Task<ImportResult> ImportAnalysisAsync(string xml, string? siteName = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return new ImportResult { Success = false, Message = "Analysis XML is empty." };

        var endpoints = new List<string>();
        if (!string.IsNullOrWhiteSpace(siteName))
            endpoints.Add($"api/analyses/{Uri.EscapeDataString(siteName)}");
        endpoints.Add("api/analyses");

        ImportResult? last = null;
        foreach (var endpoint in endpoints)
        {
            try
            {
                // New content per attempt — StringContent can only be read once
                using var content = new StringContent(xml, Encoding.UTF8, "application/xml");
                using var resp = await _retryPolicy.ExecuteAsync(
                    e => _http.PostAsync(endpoint, content, e), ct);
                var body = await resp.Content.ReadAsStringAsync(ct);

                last = new ImportResult
                {
                    Success = resp.IsSuccessStatusCode,
                    HttpStatus = (int)resp.StatusCode,
                    Message = resp.IsSuccessStatusCode ? body : $"HTTP {(int)resp.StatusCode}: {body}",
                    FixletId = TryParseFixletId(body) ?? TryParseIdFromLocation(resp)
                };

                if (last.Success)
                    return last;

                // 404 on site-specific path → try next endpoint
                if (resp.StatusCode != System.Net.HttpStatusCode.NotFound)
                    return last;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import analysis via {Endpoint}", endpoint);
                last = new ImportResult { Success = false, Message = ex.Message };
            }
        }

        return last ?? new ImportResult { Success = false, Message = "No analysis endpoint attempted." };
    }

    public async Task<ImportResult> CreateAutomaticGroupAsync(string xml, CancellationToken ct = default)
    {
        using var content = new StringContent(xml, Encoding.UTF8, "application/xml");
        using var resp = await _retryPolicy.ExecuteAsync(
            ct => _http.PostAsync("api/groups", content, ct), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            using var alt = new StringContent(xml, Encoding.UTF8, "application/xml");
            using var altResp = await _http.PostAsync("api/computergroups", alt, ct);
            var altBody = await altResp.Content.ReadAsStringAsync(ct);
            return new ImportResult
            {
                Success = altResp.IsSuccessStatusCode,
                HttpStatus = (int)altResp.StatusCode,
                Message = altResp.IsSuccessStatusCode ? altBody : $"HTTP {(int)altResp.StatusCode}: {altBody}",
                FixletId = TryParseFixletId(altBody) ?? TryParseIdFromLocation(altResp)
            };
        }

        return new ImportResult
        {
            Success = resp.IsSuccessStatusCode,
            HttpStatus = (int)resp.StatusCode,
            Message = resp.IsSuccessStatusCode ? body : $"HTTP {(int)resp.StatusCode}: {body}",
            FixletId = TryParseFixletId(body) ?? TryParseIdFromLocation(resp)
        };
    }

    public async Task<ImportResult> StartActionAsync(string xml, CancellationToken ct = default)
    {
        using var content = new StringContent(xml, Encoding.UTF8, "application/xml");
        using var resp = await _retryPolicy.ExecuteAsync(
            ct => _http.PostAsync("api/actions", content, ct), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        long? id = TryParseFixletId(body);
        if (id is null)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array &&
                    arr.GetArrayLength() > 0)
                {
                    id = arr[0].TryGetProperty("ID", out var first) || arr[0].TryGetProperty("id", out first)
                        ? first.GetInt64()
                        : null;
                }
            }
            catch
            {
                // ignore
            }
        }

        return new ImportResult
        {
            Success = resp.IsSuccessStatusCode,
            HttpStatus = (int)resp.StatusCode,
            Message = resp.IsSuccessStatusCode ? body : $"HTTP {(int)resp.StatusCode}: {body}",
            FixletId = id
        };
    }

    public async Task<ActionStatus?> GetActionStatusAsync(long actionId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"api/action/{actionId}", ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                root = root[0];
            else if (root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0)
                root = v[0];

            return new ActionStatus
            {
                Id = actionId,
                Name = ReadString(root, "Name", "Title", "title"),
                RelevantComputers = ReadOptionalInt64(root, "NumberOfComputers", "RelevantComputerCount", "TotalComputerCount"),
                Taken = ReadOptionalInt64(root, "NumberOfComputersDone", "Taken", "CompletedCount"),
                Failed = ReadOptionalInt64(root, "NumberOfComputersFailed", "FailedCount"),
                Pending = ReadOptionalInt64(root, "NumberOfComputersPending", "PendingCount")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read action {ActionId} status", actionId);
            return null;
        }
    }

    public async Task<string?> EvaluateRelevanceAsync(string relevance, CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["relevance"] = relevance,
            ["format"] = "json"
        });

        using var resp = await _http.PostAsync("api/relevance", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Relevance evaluation failed: HTTP {Status} {Body}", (int)resp.StatusCode, err);
            return null;
        }

        return await resp.Content.ReadAsStringAsync(ct);
    }

    private static long? TryParseIdFromLocation(HttpResponseMessage resp)
    {
        if (resp.Headers.Location is { } loc)
        {
            var s = loc.ToString();
            var m = System.Text.RegularExpressions.Regex.Match(s, @"(\d+)\s*$");
            if (m.Success && long.TryParse(m.Groups[1].Value, out var id))
                return id;
        }
        return null;
    }

    private static string ReadString(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) &&
                v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? "";
        }
        return "";
    }

    private static long ReadInt64(JsonElement el, params string[] names)
    {
        return ReadOptionalInt64(el, names) ?? 0;
    }

    private static long? ReadOptionalInt64(JsonElement el, params string[] names)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n))
                    return n;
                if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s))
                    return s;
            }
        }
        return null;
    }

    public void Dispose()
    {
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}