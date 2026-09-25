using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FixletBuilder.Core.PatchFactory;

public class PatchDefinition
{
    public string Kb { get; set; } = "";
    public string Title { get; set; } = "";
    public string Product { get; set; } = "";
    public string Architecture { get; set; } = "x64";
    public string Classification { get; set; } = "";
    public DateTime? ReleaseDate { get; set; }
    public string DownloadUrl { get; set; } = "";
    public string InstallerFileName { get; set; } = "";
    public long Size { get; set; }
    public string Sha1 { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public bool RebootRequired { get; set; } = true;
    public List<string> Cves { get; set; } = new();
    public List<string> Supersedes { get; set; } = new();
    public string UpdateId { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Prerequisites { get; set; } = "";
    public string ContentKind { get; set; } = "fixlet";
    public string? GeneratedBesPath { get; set; }
    public string? PublishedId { get; set; }
}

public class PatchCycleState
{
    public string Cycle { get; set; } = "";
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public List<PatchDefinition> Patches { get; set; } = new();
    public Dictionary<string, string> PublishedFixletIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> PublishedTaskIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> DeploymentActionIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> CreatedGroups { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DefaultPath(string outputDir) =>
        Path.Combine(outputDir, "patch-cycle-state.json");

    public static PatchCycleState Load(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                return JsonSerializer.Deserialize<PatchCycleState>(File.ReadAllText(path)) ?? new PatchCycleState();
            }
            catch
            {
            }
        }
        return new PatchCycleState();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}

public class ValidationCheck
{
    public string Name { get; set; } = "";
    public bool Passed { get; set; }
    public bool Required { get; set; } = true;
    public string Message { get; set; } = "";
}

public class PatchValidationReport
{
    public string Kb { get; set; } = "";
    public List<ValidationCheck> Checks { get; set; } = new();

    public bool Passed => Checks.Where(c => c.Required).All(c => c.Passed);

    public void Add(string name, bool passed, string message = "", bool required = true) =>
        Checks.Add(new ValidationCheck { Name = name, Passed = passed, Message = message, Required = required });
}

public class PatchValidationResult
{
    public List<PatchValidationReport> Reports { get; set; } = new();
    public bool AllPassed => Reports.All(r => r.Passed);
    public int FailedCount => Reports.Count(r => !r.Passed);
}

public static class PatchStages
{
    public const string Pilot = "pilot";
    public const string Wave1 = "wave1";
    public const string Wave2 = "wave2";
    public const string Wave3 = "wave3";

    public static readonly string[] All = { Pilot, Wave1, Wave2, Wave3 };

    public static string DisplayName(string stage) => stage switch
    {
        Pilot => "Pilot",
        Wave1 => "Wave 1",
        Wave2 => "Wave 2",
        Wave3 => "Wave 3",
        _ => stage
    };

    public static bool IsValid(string stage) =>
        All.Contains(stage, StringComparer.OrdinalIgnoreCase);
}

public class ComplianceRow
{
    public string Kb { get; set; } = "";
    public string Title { get; set; } = "";
    public string Product { get; set; } = "";
    public string? ActionId { get; set; }
    public string Stage { get; set; } = "";
    public long? RelevantComputers { get; set; }
    public long? Taken { get; set; }
    public long? Failed { get; set; }
    public long? Pending { get; set; }
    public long? InstalledEstimate { get; set; }
    public string Notes { get; set; } = "";
}

public class ComplianceReport
{
    public string Cycle { get; set; } = "";
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public List<ComplianceRow> Rows { get; set; } = new();
}
