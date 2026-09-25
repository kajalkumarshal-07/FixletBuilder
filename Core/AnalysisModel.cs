using System.Text.Json.Serialization;

namespace FixletBuilder.Core;

/// <summary>
/// A single retrieved-property definition inside a BigFix Analysis.
/// Schema: developer.bigfix.com BES XML Analyses.
/// </summary>
public class AnalysisProperty
{
    public string Name { get; set; } = "";

    /// <summary>Relevance expression that evaluates to the property value.</summary>
    public string Relevance { get; set; } = "";

    /// <summary>
    /// Optional NonNegativeTimeInterval (ISO-8601 style), e.g. PT15M, PT1H, P1D.
    /// Empty = evaluate on every client report.
    /// </summary>
    public string EvaluationPeriod { get; set; } = "";

    /// <summary>Keep statistical inspection data for dashboards/wizards.</summary>
    public bool KeepStatistics { get; set; }

    /// <summary>Optional display ID; assigned 1..n when writing XML if unset/0.</summary>
    public int Id { get; set; }

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Name) ? "(unnamed property)" : Name;
}

/// <summary>
/// BigFix Analysis content: targeting relevance + one or more property expressions.
/// No Action / ActionScript / SuccessCriteria (read-only inventory content).
/// </summary>
public class AnalysisModel
{
    public string Title { get; set; } = "";
    public string Category { get; set; } = "BESPolicies";
    public string Source { get; set; } = "FixletBuilder";
    public string SourceId { get; set; } = "";
    public string Domain { get; set; } = "BESC";
    public string? SourceReleaseDate { get; set; }

    public string Description { get; set; } = "";

    /// <summary>Targeting relevance — all expressions must be true for a computer to report.</summary>
    public List<string> Relevance { get; set; } = new();

    public List<AnalysisProperty> Properties { get; set; } = new();

    public string GroupRelevance { get; set; } = "";
}
