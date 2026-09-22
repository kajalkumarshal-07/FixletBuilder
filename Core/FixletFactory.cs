using System.IO;
using Microsoft.Extensions.Logging;

namespace FixletBuilder.Core;

public static class FixletFactory
{
    private static readonly string[][] Aliases =
    {
        new[] { "title", "fixlet title" },
        new[] { "appname", "application", "app", "software", "name" },
        new[] { "version" },
        new[] { "sourceurl", "downloadurl", "url", "download", "installerurl" },
        new[] { "silentargs", "silent", "parameters", "commandline", "flags" },
        new[] { "installpath", "targetpath", "exe", "file" },
        new[] { "uninstallstring", "uninstall" },
        new[] { "relevance", "relevanceexpression" },
        new[] { "successcriteria", "success" },
        new[] { "description", "notes" },
        new[] { "category" },
        new[] { "source" },
        new[] { "sourceid", "fixlet id" },
        new[] { "domain" },
        new[] { "downloadsize", "size mb" },
        new[] { "type", "fixlettype" },
        new[] { "sha1", "hash", "sha1hash", "sha 1" },
        new[] { "sha256", "sha256hash", "sha 256", "hash256" },
        new[] { "filesize", "filesizebytes", "sizebytes", "size bytes" },
        new[] { "registrykey", "regkey", "registrykeypath", "regkeypath" },
        new[] { "registryvaluename", "regvaluename", "regvalname", "valname" },
        new[] { "registryvalue", "regvalue", "regval", "regvaldata" }
    };

    public static Dictionary<string, int> BuildColumnMap(string[] headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (headers is null) return map;
        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i]?.Trim().ToLowerInvariant() ?? "";
            if (h.Length > 0 && !map.ContainsKey(h))
                map[h] = i;
        }
        return map;
    }

    private static string Get(string[] row, Dictionary<string, int> map, params string[] names)
    {
        foreach (var name in names)
        {
            if (map.TryGetValue(name, out int idx) && idx >= 0 && idx < row.Length)
            {
                var value = row[idx]?.Trim() ?? "";
                if (value.Length > 0) return value;
            }
        }
        return "";
    }

    private static string GetByAliasGroup(string[] row, Dictionary<string, int> map, string aliasGroup)
    {
        foreach (var group in Aliases)
        {
            if (group[0] == aliasGroup)
                return Get(row, map, group);
        }
        return "";
    }

    private static string DetectType(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return FixletTemplates.TypeInstall;
        var t = raw.ToLowerInvariant();
        if (t.Contains("uninstall")) return FixletTemplates.TypeUninstall;
        if (t.Contains("upgrade")) return FixletTemplates.TypeUpgrade;
        if (t.Contains("custom")) return FixletTemplates.TypeCustom;
        return FixletTemplates.TypeInstall;
    }

    public static FixletModel Build(string[] row, Dictionary<string, int> map, string? forcedType, out List<string> issues)
    {
        issues = new List<string>();
        if (row is null) throw new ArgumentNullException(nameof(row));
        if (map is null) throw new ArgumentNullException(nameof(map));

        string type = string.IsNullOrEmpty(forcedType)
            ? DetectType(GetByAliasGroup(row, map, "type"))
            : forcedType;

        var appName = GetByAliasGroup(row, map, "appname");
        var version = GetByAliasGroup(row, map, "version");
        var sourceUrl = GetByAliasGroup(row, map, "sourceurl");
        var silentArgs = GetByAliasGroup(row, map, "silentargs");
        var installPath = GetByAliasGroup(row, map, "installpath");
        var uninstallString = GetByAliasGroup(row, map, "uninstallstring");
        var sha1 = GetByAliasGroup(row, map, "sha1");
        var sha256 = GetByAliasGroup(row, map, "sha256");
        var fileSizeStr = GetByAliasGroup(row, map, "filesize");
        long.TryParse(fileSizeStr, out long fileSizeBytes);
        var registryKeyPath = GetByAliasGroup(row, map, "registrykey");
        var registryValueName = GetByAliasGroup(row, map, "registryvaluename");
        var registryValue = GetByAliasGroup(row, map, "registryvalue");

        if (string.IsNullOrWhiteSpace(registryValueName))
            registryValueName = "DisplayVersion";

        var template = FixletTemplates.Apply(type, appName, version, sourceUrl, silentArgs, installPath, uninstallString,
            sha1: sha1, sha256: sha256, fileSizeBytes: fileSizeBytes);

        var relevance = RelevanceBuilder.BuildForInstall(installPath, appName, version,
            registryKeyPath, registryValueName, registryValue);
        if (type == "upgrade")
            relevance = RelevanceBuilder.BuildForUpgrade(installPath, appName, version, version,
                registryKeyPath, registryValueName, registryValue);
        else if (type == "uninstall")
            relevance = RelevanceBuilder.BuildForUninstall(installPath, appName, uninstallString,
                registryKeyPath, registryValueName, registryValue);

        var successCriteria = RelevanceBuilder.BuildSuccessCriteria(installPath, type, version,
            registryKeyPath, registryValueName);

        var model = new FixletModel
        {
            Title = GetByAliasGroup(row, map, "title"),
            Description = GetByAliasGroup(row, map, "description"),
            Category = GetByAliasGroup(row, map, "category"),
            Source = GetByAliasGroup(row, map, "source"),
            SourceId = GetByAliasGroup(row, map, "sourceid"),
            Domain = GetByAliasGroup(row, map, "domain"),
            ActionDescription = template.ActionDescription,
            Sha1 = sha1,
            Sha256 = sha256,
            FileSizeBytes = fileSizeBytes > 0 ? fileSizeBytes : null,
            RegistryKeyPath = registryKeyPath,
            RegistryValueName = registryValueName,
            RegistryValue = registryValue
        };

        if (model.Title.Length == 0) model.Title = template.Title;
        if (model.Description.Length == 0) model.Description = template.Description;
        if (model.Category.Length == 0) model.Category = "Applications";

        if (double.TryParse(GetByAliasGroup(row, map, "downloadsize"),
                System.Globalization.CultureInfo.InvariantCulture, out double size) && size > 0)
        {
            model.DownloadSizeMb = size;
        }

        var rel = GetByAliasGroup(row, map, "relevance");
        if (rel.Length > 0)
            model.Relevance = rel.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        else
            model.Relevance = relevance;

        model.ActionScript = template.ActionScript;
        model.SuccessCriteria = GetByAliasGroup(row, map, "successcriteria");
        if (model.SuccessCriteria.Length == 0)
            model.SuccessCriteria = successCriteria;

        if (model.Title.Length == 0)
            issues.Add("Row has no Title or AppName - skipped.");
        else if (appName.Length == 0)
            issues.Add($"'{model.Title}': AppName column is empty.");

        if ((type == FixletTemplates.TypeInstall || type == FixletTemplates.TypeUpgrade) && sourceUrl.Length == 0)
            issues.Add($"'{model.Title}': no installer URL - the action will not download anything.");

        if (type == FixletTemplates.TypeUninstall && uninstallString.Length == 0)
            issues.Add($"'{model.Title}': no UninstallString - the action will not remove anything.");

        if (model.Relevance.Count == 0)
            issues.Add($"'{model.Title}': no relevance expressions - the fixlet will apply to every computer.");

        return model;
    }

    public static List<string> Validate(FixletModel m)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(m.Title))
            issues.Add("Title is required.");
        if (m.Relevance.Count == 0)
            issues.Add("At least one relevance expression is required.");
        if (string.IsNullOrWhiteSpace(m.ActionScript))
            issues.Add("Action script is empty.");
        if (string.IsNullOrWhiteSpace(m.SuccessCriteria))
            issues.Add("Success criteria is empty.");
        if (m.ActionScript?.Length > 100_000)
            issues.Add("Action script exceeds 100,000 character limit.");
        if (m.Relevance.Any(r => r.Length > 8192))
            issues.Add("One or more relevance expressions exceed 8,192 character limit.");
        return issues;
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "fixlet";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim();
        return result.Length > 200 ? result[..200] : result;
    }
}