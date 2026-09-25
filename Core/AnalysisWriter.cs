using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace FixletBuilder.Core;

public sealed partial class AnalysisWriter : IAnalysisWriter
{
    private readonly ILogger<AnalysisWriter> _logger;

    public AnalysisWriter(ILogger<AnalysisWriter>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalysisWriter>.Instance;
    }

    public string GenerateXml(AnalysisModel m)
    {
        ArgumentNullException.ThrowIfNull(m);

        var analysis = new XElement("Analysis");

        analysis.Add(new XElement("Title", SanitizeForXml(m.Title)));

        if (!string.IsNullOrWhiteSpace(m.Description))
            analysis.Add(new XElement("Description", new XCData(SanitizeForXml(m.Description))));

        // Targeting relevance is required [1..*]
        var targets = (m.Relevance ?? new List<string>())
            .Select(r => r?.Trim() ?? "")
            .Where(r => r.Length > 0)
            .ToList();

        if (targets.Count == 0)
            targets.Add("true");

        foreach (var rel in targets)
        {
            if (rel.Length > MaxRelevanceLength)
                throw new InvalidException($"Relevance expression too long ({rel.Length} > {MaxRelevanceLength}).");
            analysis.Add(new XElement("Relevance", rel));
        }

        if (!string.IsNullOrWhiteSpace(m.Category))
            analysis.Add(new XElement("Category", SanitizeForXml(m.Category)));

        if (!string.IsNullOrWhiteSpace(m.Source))
            analysis.Add(new XElement("Source", SanitizeForXml(m.Source)));

        var releaseDate = string.IsNullOrWhiteSpace(m.SourceReleaseDate)
            ? DateTime.UtcNow.ToString("yyyy-MM-dd")
            : m.SourceReleaseDate;
        analysis.Add(new XElement("SourceReleaseDate", SanitizeForXml(releaseDate)));

        if (!string.IsNullOrWhiteSpace(m.SourceId))
            analysis.Add(new XElement("SourceID", SanitizeForXml(m.SourceId)));

        if (!string.IsNullOrWhiteSpace(m.Domain))
            analysis.Add(new XElement("Domain", SanitizeForXml(m.Domain)));

        // Properties: unique non-negative integer IDs required by BES.xsd
        var props = (m.Properties ?? new List<AnalysisProperty>())
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Relevance))
            .ToList();

        if (props.Count == 0)
            throw new InvalidException("Analysis requires at least one property with Name and Relevance.");

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIds = new HashSet<int>();
        int autoId = 1;

        foreach (var p in props)
        {
            var name = SanitizeForXml(p.Name.Trim());
            if (!seenNames.Add(name))
                throw new InvalidException($"Duplicate analysis property name: '{name}'.");
            if (p.Relevance.Trim().Length > MaxRelevanceLength)
                throw new InvalidException($"Property '{name}' relevance too long.");

            var id = p.Id > 0 ? p.Id : autoId;
            while (seenIds.Contains(id)) id++;
            seenIds.Add(id);
            if (p.Id <= 0) autoId = Math.Max(autoId, id + 1);

            var el = new XElement("Property",
                new XAttribute("Name", name),
                new XAttribute("ID", id));

            var period = NormalizeEvaluationPeriod(p.EvaluationPeriod);
            if (period.Length > 0)
                el.Add(new XAttribute("EvaluationPeriod", period));

            if (p.KeepStatistics)
                el.Add(new XAttribute("KeepStatistics", "true"));

            el.Add(new XCData(p.Relevance.Trim()));
            analysis.Add(el);
        }

        if (!string.IsNullOrWhiteSpace(m.GroupRelevance))
            analysis.Add(new XElement("GroupRelevance", m.GroupRelevance.Trim()));

        var xsi = XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance");
        var body = new XElement("BES",
            new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XAttribute(xsi + "noNamespaceSchemaLocation", "BES.xsd"),
            analysis);

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + body.ToString();
    }

    public void WriteBes(string filePath, AnalysisModel model)
    {
        var fullPath = FixletWriter.ValidateOutputPath(filePath);
        var xml = GenerateXml(model);
        File.WriteAllText(fullPath, xml, new UTF8Encoding(false));
        _logger.LogInformation("Wrote analysis: {Path} ({Bytes} bytes, {Props} properties)",
            fullPath, xml.Length, model.Properties.Count);
    }

    public void WriteJson(string filePath, AnalysisModel model)
    {
        var fullPath = FixletWriter.ValidateOutputPath(filePath);
        var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fullPath, json, new UTF8Encoding(false));
        _logger.LogInformation("Wrote analysis JSON: {Path}", fullPath);
    }

    public List<string> Validate(AnalysisModel m)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(m.Title))
            issues.Add("Title is required.");

        var targets = (m.Relevance ?? new()).Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (targets.Count == 0)
            issues.Add("At least one targeting relevance expression is required (use 'true' for all computers).");

        var props = m.Properties ?? new();
        var named = props.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
        if (named.Count == 0)
            issues.Add("At least one property is required.");
        else
        {
            foreach (var p in named)
            {
                if (string.IsNullOrWhiteSpace(p.Relevance))
                    issues.Add($"Property '{p.Name}': relevance is empty.");
                else if (p.Relevance.Length > MaxRelevanceLength)
                    issues.Add($"Property '{p.Name}': relevance exceeds {MaxRelevanceLength} characters.");

                if (!string.IsNullOrWhiteSpace(p.EvaluationPeriod) &&
                    NormalizeEvaluationPeriod(p.EvaluationPeriod).Length == 0)
                {
                    issues.Add($"Property '{p.Name}': invalid EvaluationPeriod '{p.EvaluationPeriod}' " +
                               "(use ISO-8601 duration like PT15M, PT1H, P1D, or leave empty).");
                }
            }

            var dupNames = named.GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);
            foreach (var d in dupNames)
                issues.Add($"Duplicate property name: '{d}'.");

            var explicitIds = named.Where(p => p.Id > 0).Select(p => p.Id).ToList();
            var dupIds = explicitIds.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (var d in dupIds)
                issues.Add($"Duplicate property ID: {d}.");
        }

        foreach (var rel in targets)
        {
            if (rel.Length > MaxRelevanceLength)
                issues.Add($"Targeting relevance exceeds {MaxRelevanceLength} characters.");
        }

        return issues;
    }

    private const int MaxRelevanceLength = 8192;

    [GeneratedRegex(@"^P(?!$)(\d+D)?(T(?=\d)(\d+H)?(\d+M)?(\d+(\.\d+)?S)?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex IsoDuration();

    /// <summary>
    /// Accepts ISO-8601 NonNegativeTimeInterval values (PT15M, PT1H, P1D, PT0S…).
    /// Empty / "every report" / "0" → omit attribute.
    /// </summary>
    public static string NormalizeEvaluationPeriod(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var v = raw.Trim();

        if (v.Equals("every report", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("everyreport", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("0", StringComparison.Ordinal) ||
            v.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("default", StringComparison.OrdinalIgnoreCase))
            return "";

        // Friendly shortcuts
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["5m"] = "PT5M", ["10m"] = "PT10M", ["15m"] = "PT15M", ["30m"] = "PT30M",
            ["1h"] = "PT1H", ["2h"] = "PT2H", ["4h"] = "PT4H", ["6h"] = "PT6H",
            ["8h"] = "PT8H", ["12h"] = "PT12H",
            ["1d"] = "P1D", ["2d"] = "P2D", ["7d"] = "P7D", ["15d"] = "P15D", ["30d"] = "P30D",
            ["hourly"] = "PT1H", ["daily"] = "P1D", ["weekly"] = "P7D",
            ["pt5m"] = "PT5M", ["pt15m"] = "PT15M", ["pt30m"] = "PT30M",
            ["pt1h"] = "PT1H", ["pt2h"] = "PT2H", ["pt4h"] = "PT4H", ["pt8h"] = "PT8H",
            ["pt12h"] = "PT12H", ["p1d"] = "P1D", ["p7d"] = "P7D"
        };

        if (map.TryGetValue(v, out var mapped))
            return mapped;

        if (IsoDuration().IsMatch(v))
            return v.ToUpperInvariant();

        return "";
    }

    private static string SanitizeForXml(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        char[] invalid =
        {
            '\x00', '\x01', '\x02', '\x03', '\x04', '\x05', '\x06', '\x07',
            '\x08', '\x0B', '\x0C', '\x0E', '\x0F', '\x10', '\x11', '\x12',
            '\x13', '\x14', '\x15', '\x16', '\x17', '\x18', '\x19', '\x1A',
            '\x1B', '\x1C', '\x1D', '\x1E', '\x1F'
        };
        return invalid.Aggregate(value, (current, c) => current.Replace(c, ' '));
    }
}
