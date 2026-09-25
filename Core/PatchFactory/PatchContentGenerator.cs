using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FixletBuilder.Core.PatchFactory;

public class ProductProfile
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string OsName { get; set; } = "";
    public string MinVersion { get; set; } = "";
    public string MaxVersionExclusive { get; set; } = "";
    public string[] Architectures { get; set; } = { "x64" };
    public bool Server { get; set; }
}

public static class PatchRelevance
{
    private static readonly ProductProfile[] Profiles =
    {
        new()
        {
            Key = "win11-24h2",
            DisplayName = "Windows 11 24H2",
            OsName = "Win11",
            MinVersion = "10.0.26100",
            MaxVersionExclusive = "10.0.27000",
            Architectures = new[] { "x64", "arm64" }
        },
        new()
        {
            Key = "win11-23h2",
            DisplayName = "Windows 11 23H2",
            OsName = "Win11",
            MinVersion = "10.0.22631",
            MaxVersionExclusive = "10.0.26100",
            Architectures = new[] { "x64", "arm64" }
        },
        new()
        {
            Key = "win11-22h2",
            DisplayName = "Windows 11 22H2",
            OsName = "Win11",
            MinVersion = "10.0.22621",
            MaxVersionExclusive = "10.0.22631",
            Architectures = new[] { "x64", "arm64" }
        },
        new()
        {
            Key = "win10-22h2",
            DisplayName = "Windows 10 22H2",
            OsName = "Win10",
            MinVersion = "10.0.19045",
            MaxVersionExclusive = "10.0.19046",
            Architectures = new[] { "x64", "x86" }
        },
        new()
        {
            Key = "server-2025",
            DisplayName = "Windows Server 2025",
            OsName = "WinServer",
            MinVersion = "10.0.26100",
            MaxVersionExclusive = "10.0.27000",
            Architectures = new[] { "x64" },
            Server = true
        },
        new()
        {
            Key = "server-2022",
            DisplayName = "Windows Server 2022",
            OsName = "WinServer",
            MinVersion = "10.0.20348",
            MaxVersionExclusive = "10.0.20349",
            Architectures = new[] { "x64" },
            Server = true
        },
        new()
        {
            Key = "server-2019",
            DisplayName = "Windows Server 2019",
            OsName = "WinServer",
            MinVersion = "10.0.17763",
            MaxVersionExclusive = "10.0.17764",
            Architectures = new[] { "x64" },
            Server = true
        },
        new()
        {
            Key = "server-2016",
            DisplayName = "Windows Server 2016",
            OsName = "WinServer",
            MinVersion = "10.0.14393",
            MaxVersionExclusive = "10.0.14394",
            Architectures = new[] { "x64" },
            Server = true
        }
    };

    public static IReadOnlyList<ProductProfile> All => Profiles;

    public static bool TryGetProfile(string? productTitle, out ProductProfile? profile)
    {
        profile = null;
        if (string.IsNullOrWhiteSpace(productTitle))
            return false;

        var text = productTitle.ToLowerInvariant();

        foreach (var p in Profiles)
        {
            if (Matches(text, p))
            {
                profile = p;
                return true;
            }
        }
        return false;
    }

    private static bool Matches(string text, ProductProfile p)
    {
        switch (p.Key)
        {
            case "win11-24h2":
                return text.Contains("windows 11") && (text.Contains("24h2") || text.Contains("26100"));
            case "win11-23h2":
                return text.Contains("windows 11") && (text.Contains("23h2") || text.Contains("22631"));
            case "win11-22h2":
                return text.Contains("windows 11") && (text.Contains("22h2") || text.Contains("22621")) &&
                       !text.Contains("23h2") && !text.Contains("24h2");
            case "win10-22h2":
                return text.Contains("windows 10") && (text.Contains("22h2") || text.Contains("19045"));
            case "server-2025":
                return (text.Contains("server 2025") || text.Contains("2025")) && text.Contains("server");
            case "server-2022":
                return text.Contains("server 2022") || text.Contains("20348");
            case "server-2019":
                return text.Contains("server 2019") || text.Contains("17763") ||
                       text.Contains("windows server 2019");
            case "server-2016":
                return text.Contains("server 2016") || text.Contains("14393");
            default:
                return false;
        }
    }

    public static List<string> BuildRelevance(PatchDefinition patch, ProductProfile profile)
    {
        var relevance = new List<string>();

        if (!string.Equals(profile.OsName, "WinServer", StringComparison.Ordinal) ||
            profile.Key is "server-2025" or "server-2022" or "server-2019" or "server-2016")
        {
            relevance.Add($"(name of operating system = \"{profile.OsName}\")");
        }

        relevance.Add($"(version of operating system as string as version >= \"{profile.MinVersion}\" as version)");
        relevance.Add($"(version of operating system as string as version < \"{profile.MaxVersionExclusive}\" as version)");

        if (profile.Architectures.Contains("x64") && patch.Architecture.Equals("x64", StringComparison.OrdinalIgnoreCase))
            relevance.Add("(x64 of operating system)");
        else if (profile.Architectures.Contains("x86") && patch.Architecture.Equals("x86", StringComparison.OrdinalIgnoreCase))
            relevance.Add("(not x64 of operating system)");
        else if (profile.Architectures.Contains("arm64") && patch.Architecture.Equals("arm64", StringComparison.OrdinalIgnoreCase))
            relevance.Add("(arm64 of operating system)");

        if (!string.IsNullOrWhiteSpace(patch.Kb))
            relevance.Add(BuildKbNotInstalled(patch.Kb));

        return relevance;
    }

    public static string BuildKbNotInstalled(string kb)
    {
        var id = NormalizeKb(kb);
        return $"not exists keys whose (name of it contains \"{id}\") " +
               "of keys \"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Component Based Servicing\\Packages\" " +
               "of native registry";
    }

    public static string BuildKbInstalled(string kb)
    {
        var id = NormalizeKb(kb);
        return $"exists keys whose (name of it contains \"{id}\") " +
               "of keys \"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Component Based Servicing\\Packages\" " +
               "of native registry";
    }

    public static string BuildSuccessCriteria(PatchDefinition patch)
    {
        if (string.IsNullOrWhiteSpace(patch.Kb))
            return "true";
        return BuildKbInstalled(patch.Kb);
    }

    public static string BuildEstateInstalledRelevance(string kb)
    {
        var id = NormalizeKb(kb);
        return $"(names of computers whose (exists keys whose (name of it contains \"{id}\") " +
               "of keys \"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Component Based Servicing\\Packages\" " +
               "of native registry of it))";
    }

    public static string NormalizeKb(string kb)
    {
        var id = kb.Trim().ToUpperInvariant();
        if (!id.StartsWith("KB", StringComparison.Ordinal))
            id = "KB" + Regex.Replace(id, "^0+", "");
        else
            id = "KB" + Regex.Replace(id[2..], "^0+", "");
        return id;
    }
}

public static class PatchContentGenerator
{
    public static FixletModel BuildFixletModel(PatchDefinition patch, out string error)
    {
        error = "";
        if (!PatchRelevance.TryGetProfile(patch.Product, out var profile) || profile is null)
        {
            error = $"No OS profile for product '{patch.Product}'";
        }

        var script = BuildActionScript(patch, out var scriptError);
        if (!string.IsNullOrEmpty(scriptError))
            error = error.Length > 0 ? error + "; " + scriptError : scriptError;

        var title = string.IsNullOrWhiteSpace(patch.Kb)
            ? $"Security Update - {patch.Product}"
            : $"Monthly Patch - {patch.Kb} - {profile?.DisplayName ?? patch.Product}";

        if (profile is not null && !string.IsNullOrWhiteSpace(patch.Kb))
            title = $"Monthly Patch - {patch.Kb} - {profile.DisplayName} {patch.Architecture}";

        var description = BuildDescription(patch, profile);

        return new FixletModel
        {
            Title = title,
            Category = "Security Updates",
            Source = "Microsoft",
            SourceId = string.IsNullOrWhiteSpace(patch.Kb) ? patch.UpdateId : patch.Kb,
            Domain = "FixletBuilder Patch Factory",
            Description = description,
            Relevance = profile is not null
                ? PatchRelevance.BuildRelevance(patch, profile)
                : new List<string> { "false" },
            ActionDescription = $"Install {patch.Kb}",
            ActionScript = script,
            SuccessCriteria = PatchRelevance.BuildSuccessCriteria(patch),
            Sha1 = patch.Sha1,
            Sha256 = patch.Sha256,
            FileSizeBytes = patch.Size > 0 ? patch.Size : null,
            DownloadSizeMb = patch.Size > 0 ? Math.Round(patch.Size / (1024.0 * 1024.0), 2) : null
        };
    }

    public static string BuildActionScript(PatchDefinition patch, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(patch.DownloadUrl))
        {
            error = "Download URL missing";
            return "";
        }
        if (string.IsNullOrWhiteSpace(patch.Sha1) || string.IsNullOrWhiteSpace(patch.Sha256))
        {
            error = "SHA1/SHA256 missing";
            return "";
        }
        if (patch.Size <= 0)
        {
            error = "File size missing";
            return "";
        }

        var fileName = string.IsNullOrWhiteSpace(patch.InstallerFileName)
            ? DeriveFileName(patch.DownloadUrl, patch.Kb)
            : SanitizeFileToken(patch.InstallerFileName);

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var lines = new List<string>
        {
            $"prefetch {fileName} sha1:{patch.Sha1} size:{patch.Size} \"{patch.DownloadUrl}\" sha256:{patch.Sha256}",
            ""
        };

        switch (ext)
        {
            case ".msu":
                lines.Add($"waithidden wusa.exe \"__download\\{fileName}\" /quiet /norestart");
                break;
            case ".cab":
                lines.Add($"waithidden dism.exe /Online /Add-Package /PackagePath:\"__download\\{fileName}\" /Quiet /NoRestart");
                break;
            case ".msp":
                lines.Add($"waithidden msiexec.exe /update \"__download\\{fileName}\" /qn /norestart");
                break;
            case ".msi":
                lines.Add($"waithidden msiexec.exe /i \"__download\\{fileName}\" /qn /norestart");
                break;
            default:
                lines.Add($"waithidden \"__download\\{fileName}\" /quiet /norestart");
                break;
        }

        lines.Add("");
        lines.Add("parameter \"PatchExitCode\" = \"{exit code of action}\"");
        lines.Add("");
        lines.Add("if {parameter \"PatchExitCode\" = \"3010\" or parameter \"PatchExitCode\" = \"1641\"}");
        lines.Add("    action requires restart");
        lines.Add("endif");
        lines.Add("if {parameter \"PatchExitCode\" = \"0\"}");
        if (patch.RebootRequired)
            lines.Add("    action may require restart");
        else
            lines.Add("    // success - reboot not expected");
        lines.Add("endif");

        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildBesXml(FixletModel model, bool asTask)
    {
        var writer = new FixletWriter(NullLogger<FixletWriter>.Instance);
        var fixletXml = writer.GenerateXml(model);

        if (!asTask)
            return fixletXml;

        var doc = System.Xml.Linq.XDocument.Parse(fixletXml);
        var fixlet = doc.Root?.Element("Fixlet");
        if (fixlet is not null)
        {
            var task = new System.Xml.Linq.XElement("Task", fixlet.Attributes());
            foreach (var child in fixlet.Elements())
                task.Add(child);
            fixlet.ReplaceWith(task);
        }
        return doc.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    private static string BuildDescription(PatchDefinition patch, ProductProfile? profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<h2>{System.Security.SecurityElement.Escape(patch.Title)}</h2>");
        sb.AppendLine("<p>Monthly Microsoft Security Update generated by FixletBuilder Patch Factory.</p>");
        sb.AppendLine("<ul>");
        if (!string.IsNullOrWhiteSpace(patch.Kb))
            sb.AppendLine($"<li><b>KB:</b> {System.Security.SecurityElement.Escape(patch.Kb)}</li>");
        if (profile is not null)
            sb.AppendLine($"<li><b>Product:</b> {System.Security.SecurityElement.Escape(profile.DisplayName)}</li>");
        sb.AppendLine($"<li><b>Architecture:</b> {System.Security.SecurityElement.Escape(patch.Architecture)}</li>");
        if (patch.ReleaseDate is { } dt)
            sb.AppendLine($"<li><b>Release date:</b> {dt:yyyy-MM-dd}</li>");
        sb.AppendLine($"<li><b>Reboot required:</b> {(patch.RebootRequired ? "Yes" : "No")}</li>");
        if (patch.Cves.Count > 0)
            sb.AppendLine($"<li><b>CVEs:</b> {System.Security.SecurityElement.Escape(string.Join(", ", patch.Cves))}</li>");
        if (patch.Supersedes.Count > 0)
            sb.AppendLine($"<li><b>Supersedes:</b> {System.Security.SecurityElement.Escape(string.Join(", ", patch.Supersedes))}</li>");
        if (!string.IsNullOrWhiteSpace(patch.Prerequisites))
            sb.AppendLine($"<li><b>Prerequisites:</b> {System.Security.SecurityElement.Escape(patch.Prerequisites)}</li>");
        sb.AppendLine($"<li><b>Generated:</b> {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</li>");
        sb.AppendLine("</ul>");
        return sb.ToString();
    }

    private static string DeriveFileName(string url, string kb)
    {
        try
        {
            var name = Path.GetFileName(new Uri(url).LocalPath);
            if (!string.IsNullOrEmpty(name))
                return SanitizeFileToken(name);
        }
        catch
        {
        }
        return SanitizeFileToken(string.IsNullOrWhiteSpace(kb) ? "update.msu" : kb + ".msu");
    }

    private static string SanitizeFileToken(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Replace(" ", "_");
    }
}
