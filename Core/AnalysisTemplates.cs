namespace FixletBuilder.Core;

public class AnalysisTemplate
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "BESPolicies";
    public List<string> Relevance { get; set; } = new() { "true" };
    public List<AnalysisProperty> Properties { get; set; } = new();
    public string DefaultEvaluationPeriod { get; set; } = "";
}

/// <summary>
/// Built-in analysis templates with property relevance patterns
/// sourced from HCL BigFix docs / common inventory practices.
/// </summary>
public static class AnalysisTemplates
{
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        "app-status",
        "app-version",
        "software-inventory",
        "os-overview",
        "disk-space",
        "bigfix-components",
        "custom"
    };

    public static AnalysisTemplate Get(string key, string appName = "", string registryKey = "",
        string registryValueName = "", string registryValue = "", string installPath = "")
    {
        key = (key ?? "custom").Trim().ToLowerInvariant();

        // Friendly aliases
        if (key is "inventory" or "software" or "apps") key = "software-inventory";
        if (key is "os" or "operating-system") key = "os-overview";
        if (key is "disk" or "drives" or "storage") key = "disk-space";
        if (key is "client" or "bigfix" or "component-versions") key = "bigfix-components";
        if (key is "version" or "appversion") key = "app-version";
        if (key is "installed" or "status") key = "app-status";

        return key switch
        {
            "app-status" => AppStatus(appName, registryKey, registryValueName, registryValue, installPath),
            "app-version" => AppVersion(appName, registryKey, registryValueName, registryValue, installPath),
            "software-inventory" => SoftwareInventory(appName),
            "os-overview" => OsOverview(),
            "disk-space" => DiskSpace(),
            "bigfix-components" => BigFixComponents(),
            _ => Custom(appName)
        };
    }

    public static AnalysisTemplate AppStatus(string appName, string registryKey,
        string registryValueName, string registryValue, string installPath)
    {
        appName = string.IsNullOrWhiteSpace(appName) ? "Target Application" : appName.Trim();
        registryValueName = string.IsNullOrWhiteSpace(registryValueName) ? "DisplayVersion" : registryValueName;

        var displayNameLower = appName.ToLowerInvariant();
        var props = new List<AnalysisProperty>();

        if (!string.IsNullOrWhiteSpace(registryKey))
        {
            SplitRegKey(registryKey, out var parent, out var leaf);
            var valueCond = !string.IsNullOrWhiteSpace(registryValue)
                ? $"value \"{registryValueName}\" of it as string = \"{registryValue}\""
                : $"exists value \"{registryValueName}\" of it";

            props.Add(new AnalysisProperty
            {
                Name = $"{appName} Installed",
                Relevance = string.IsNullOrEmpty(leaf)
                    ? $"exists key \"{parent}\" of native registry"
                    : $"exists keys \"{leaf}\" whose ({valueCond}) of keys \"{parent}\" of native registry",
                EvaluationPeriod = "PT1H"
            });

            props.Add(new AnalysisProperty
            {
                Name = $"{appName} Version",
                Relevance = string.IsNullOrEmpty(leaf)
                    ? $"(value \"{registryValueName}\" of it as string) of key \"{parent}\" of native registry | \"Not Installed\""
                    : $"(values \"{registryValueName}\" of it as string) of key \"{leaf}\" of keys \"{parent}\" of native registry | \"Not Installed\"",
                EvaluationPeriod = "PT1H"
            });
        }
        else
        {
            props.Add(new AnalysisProperty
            {
                Name = $"{appName} Installed",
                Relevance = string.IsNullOrWhiteSpace(installPath)
                    ? $"exists folder \"C:\\Program Files\\{appName}\" or exists folder \"C:\\Program Files (x86)\\{appName}\""
                    : $"exists file \"{installPath}\"",
                EvaluationPeriod = "PT1H"
            });

            props.Add(new AnalysisProperty
            {
                Name = $"{appName} Version",
                Relevance = string.IsNullOrWhiteSpace(installPath)
                    ? $"if exists file \"C:\\Program Files\\{appName}\\{appName}.exe\" then version of file \"C:\\Program Files\\{appName}\\{appName}.exe\" as string else \"Not Installed\""
                    : $"if exists file \"{installPath}\" then version of file \"{installPath}\" as string else \"Not Installed\"",
                EvaluationPeriod = "PT1H"
            });
        }

        // Registry DisplayName search (works even without explicit key)
        props.Add(new AnalysisProperty
        {
            Name = $"{appName} Display Version (Uninstall key)",
            Relevance =
                $"if exists keys whose (value \"DisplayName\" of it as string as lowercase contains \"{displayNameLower}\") " +
                $"of key \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\" of native registry " +
                $"then (values \"DisplayVersion\" of it as string) of keys whose (value \"DisplayName\" of it as string as lowercase contains \"{displayNameLower}\") " +
                $"of key \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\" of native registry " +
                $"else \"Not Installed\"",
            EvaluationPeriod = "PT1H"
        });

        return new AnalysisTemplate
        {
            Key = "app-status",
            Title = $"{appName} - Install Status",
            Description = $"<h2>{appName} install status</h2><p>Reports whether {appName} is installed and its version.</p>",
            Category = "BESPolicies",
            Relevance = new List<string> { "true" },
            Properties = props,
            DefaultEvaluationPeriod = "PT1H"
        };
    }

    public static AnalysisTemplate AppVersion(string appName, string registryKey,
        string registryValueName, string registryValue, string installPath)
    {
        var t = AppStatus(appName, registryKey, registryValueName, registryValue, installPath);
        t.Key = "app-version";
        t.Title = $"{(string.IsNullOrWhiteSpace(appName) ? "Application" : appName)} - Version Inventory";
        t.Description = $"<h2>Version inventory</h2><p>Retrieved properties for {(string.IsNullOrWhiteSpace(appName) ? "the application" : appName)}.</p>";
        // Keep version-focused properties
        t.Properties = t.Properties
            .Where(p => p.Name.Contains("Version", StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Contains("Installed", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return t;
    }

    public static AnalysisTemplate SoftwareInventory(string filter)
    {
        filter = (filter ?? "").Trim().ToLowerInvariant();
        var where = string.IsNullOrEmpty(filter)
            ? ""
            : $" whose (value \"DisplayName\" of it as string as lowercase contains \"{filter}\")";

        var uninstall = "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall";
        var uninstall32 = "HKLM\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall";

        return new AnalysisTemplate
        {
            Key = "software-inventory",
            Title = string.IsNullOrEmpty(filter)
                ? "Installed Software Inventory (Windows)"
                : $"Installed Software - {filter}",
            Description = "<h2>Installed software</h2><p>Display names and versions from the Windows Uninstall registry keys (64-bit and 32-bit).</p>",
            Category = "BESPolicies",
            Relevance = new List<string> { "true" },
            DefaultEvaluationPeriod = "PT4H",
            Properties = new List<AnalysisProperty>
            {
                new()
                {
                    Name = "Installed Applications (x64)",
                    Relevance =
                        $"if exists key \"{uninstall}\" of native registry " +
                        $"then (values \"DisplayName\" of it as string) of keys{where} of key \"{uninstall}\" of native registry " +
                        $"else \"None\"",
                    EvaluationPeriod = "PT4H"
                },
                new()
                {
                    Name = "Installed Applications (x86)",
                    Relevance =
                        $"if exists key \"{uninstall32}\" of native registry " +
                        $"then (values \"DisplayName\" of it as string) of keys{where} of key \"{uninstall32}\" of native registry " +
                        $"else \"None\"",
                    EvaluationPeriod = "PT4H"
                },
                new()
                {
                    Name = "Application Versions (x64)",
                    Relevance =
                        $"if exists key \"{uninstall}\" of native registry " +
                        $"then (values \"DisplayName\" of it as string, values \"DisplayVersion\" of it as string) " +
                        $"of keys{where} of key \"{uninstall}\" of native registry else \"None\"",
                    EvaluationPeriod = "PT4H"
                },
                new()
                {
                    Name = "Registered Applications (regapps)",
                    Relevance = "(names of it, versions of it) of regapps",
                    EvaluationPeriod = "P1D",
                    KeepStatistics = false
                }
            }
        };
    }

    public static AnalysisTemplate OsOverview()
    {
        return new AnalysisTemplate
        {
            Key = "os-overview",
            Title = "OS Overview",
            Description = "<h2>Operating system overview</h2><p>OS name, version, architecture, build, and client version.</p>",
            Category = "BESPolicies",
            Relevance = new List<string> { "true" },
            DefaultEvaluationPeriod = "PT1H",
            Properties = new List<AnalysisProperty>
            {
                new() { Name = "Operating System", Relevance = "name of operating system", EvaluationPeriod = "PT1H" },
                new() { Name = "OS Version", Relevance = "version of operating system as string", EvaluationPeriod = "PT1H" },
                new() { Name = "OS Architecture", Relevance = "if x64 of operating system then \"x64\" else if arm of operating system then \"arm64\" else \"x86\"", EvaluationPeriod = "PT1H" },
                new() { Name = "OS Build", Relevance = "build of operating system as string | \"n/a\"", EvaluationPeriod = "PT1H" },
                new() { Name = "Computer Name", Relevance = "name of computer", EvaluationPeriod = "PT15M" },
                new() { Name = "Domain", Relevance = "win_domain of computer | \"workgroup/n/a\"", EvaluationPeriod = "PT1H" },
                new() { Name = "IP Address", Relevance = "concatenation \", \" of (ip addresses of it | \"none\")", EvaluationPeriod = "PT15M" },
                new() { Name = "Last Restart", Relevance = "last boot time of operating system as string | \"n/a\"", EvaluationPeriod = "PT4H" },
                new() { Name = "Client Version", Relevance = "version of client", EvaluationPeriod = "PT4H" }
            }
        };
    }

    public static AnalysisTemplate DiskSpace()
    {
        return new AnalysisTemplate
        {
            Key = "disk-space",
            Title = "Disk Space",
            Description = "<h2>Disk free space</h2><p>Free space per fixed drive in GB.</p>",
            Category = "BESPolicies",
            Relevance = new List<string> { "true" },
            DefaultEvaluationPeriod = "PT1H",
            Properties = new List<AnalysisProperty>
            {
                new()
                {
                    Name = "Fixed Drives",
                    Relevance = "concatenation \", \" of (name of it | \"none\") of drives whose (drive type of it = \"Fixed\")",
                    EvaluationPeriod = "PT1H"
                },
                new()
                {
                    Name = "Free Space (GB)",
                    Relevance =
                        "concatenation \"; \" of (name of it as string & \"=\" & " +
                        "((free space of it / (1024 * 1024 * 1024)) as fixed decimal with 1 decimal place) as string & \"GB\") " +
                        "of drives whose (drive type of it = \"Fixed\")",
                    EvaluationPeriod = "PT1H"
                },
                new()
                {
                    Name = "Total Free (GB)",
                    Relevance =
                        "((sum of (free space of it) of drives whose (drive type of it = \"Fixed\")) / (1024 * 1024 * 1024)) " +
                        "as fixed decimal with 1 decimal place as string & \" GB\"",
                    EvaluationPeriod = "PT1H"
                },
                new()
                {
                    Name = "Low Disk (<10GB)",
                    Relevance =
                        "if exists drive whose (drive type of it = \"Fixed\" and free space of it < 10 * 1024 * 1024 * 1024) " +
                        "then concatenation \", \" of (name of it as string) of drives whose (drive type of it = \"Fixed\" and free space of it < 10 * 1024 * 1024 * 1024) " +
                        "else \"OK\"",
                    EvaluationPeriod = "PT1H",
                    KeepStatistics = true
                }
            }
        };
    }

    public static AnalysisTemplate BigFixComponents()
    {
        return new AnalysisTemplate
        {
            Key = "bigfix-components",
            Title = "BES Component Versions",
            Description = "<h2>BigFix component versions</h2><p>Client, Relay, Console (if present) versions.</p>",
            Category = "BESPolicies",
            Relevance = new List<string> { "true" },
            DefaultEvaluationPeriod = "PT4H",
            Properties = new List<AnalysisProperty>
            {
                new() { Name = "BES Client Version", Relevance = "version of client", EvaluationPeriod = "PT4H" },
                new()
                {
                    Name = "BES Relay Version",
                    Relevance = "if exists regapp \"BESRelay.exe\" then version of regapp \"BESRelay.exe\" as string else \"Not Installed\"",
                    EvaluationPeriod = "PT4H"
                },
                new()
                {
                    Name = "BES Console Version",
                    Relevance = "if exists regapp \"BESConsole.exe\" then version of regapp \"BESConsole.exe\" as string else \"Not Installed\"",
                    EvaluationPeriod = "P1D"
                },
                new() { Name = "Relay Choice", Relevance = "relay logo of client | \"none\"", EvaluationPeriod = "PT4H" },
                new() { Name = "Client Settings Path", Relevance = "value \"EnterpriseClient\" of key \"HKLM\\SOFTWARE\\BigFix\" of registry | \"n/a\"", EvaluationPeriod = "P1D" }
            }
        };
    }

    public static AnalysisTemplate Custom(string appName)
    {
        appName = string.IsNullOrWhiteSpace(appName) ? "My Application" : appName.Trim();
        return new AnalysisTemplate
        {
            Key = "custom",
            Title = $"{appName} Analysis",
            Description = $"<h2>{appName}</h2><p>Custom analysis — edit property relevance before saving.</p>",
            Category = "BESPolicies",
            Relevance = new List<string> { "true" },
            DefaultEvaluationPeriod = "",
            Properties = new List<AnalysisProperty>
            {
                new()
                {
                    Name = "Example Property",
                    Relevance = "name of operating system",
                    EvaluationPeriod = ""
                }
            }
        };
    }

    private static void SplitRegKey(string fullPath, out string parent, out string leaf)
    {
        var p = fullPath
            .Replace("HKEY_LOCAL_MACHINE", "HKLM", StringComparison.OrdinalIgnoreCase)
            .Replace("HKEY_CURRENT_USER", "HKCU", StringComparison.OrdinalIgnoreCase)
            .Replace("HKEY_CLASSES_ROOT", "HKCR", StringComparison.OrdinalIgnoreCase)
            .Replace("HKEY_USERS", "HKU", StringComparison.OrdinalIgnoreCase);

        var idx = p.LastIndexOf('\\');
        if (idx <= 0 || idx == p.Length - 1)
        {
            parent = p;
            leaf = "";
            return;
        }
        parent = p[..idx];
        leaf = p[(idx + 1)..];
    }
}
