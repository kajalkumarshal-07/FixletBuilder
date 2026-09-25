namespace FixletBuilder.Core;

/// <summary>
/// Detection strategy used to answer: "Is this application NOT installed, or is the installed
/// version older than the required version?"
/// </summary>
public enum DetectionMethod
{
    /// <summary>MSI product code → file version → registry key → DisplayName search → file/folder.</summary>
    Auto,
    MsiProductCode,
    FileVersion,
    RegistryKey,
    DisplayNameContains,
    FileOrFolderExists
}

/// <summary>Labels, CLI keys and priority hints for <see cref="DetectionMethod"/>.</summary>
public static class DetectionMethods
{
    public static string Key(DetectionMethod method) => method switch
    {
        DetectionMethod.MsiProductCode => "msi",
        DetectionMethod.FileVersion => "file",
        DetectionMethod.RegistryKey => "registry",
        DetectionMethod.DisplayNameContains => "displayname",
        DetectionMethod.FileOrFolderExists => "path",
        _ => "auto"
    };

    public static string Label(DetectionMethod method) => method switch
    {
        DetectionMethod.MsiProductCode => "MSI Product Code",
        DetectionMethod.FileVersion => "EXE File Version",
        DetectionMethod.RegistryKey => "Registry Key + DisplayVersion",
        DetectionMethod.DisplayNameContains => "DisplayName Contains",
        DetectionMethod.FileOrFolderExists => "File / Folder Exists",
        _ => "Auto (recommended priority)"
    };

    public static DetectionMethod Parse(string? value)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "msi":
            case "msiproductcode":
            case "msi-product-code":
            case "productcode":
            case "windowsinstaller":
                return DetectionMethod.MsiProductCode;
            case "file":
            case "fileversion":
            case "file-version":
            case "exe":
            case "exefile":
                return DetectionMethod.FileVersion;
            case "registry":
            case "registrykey":
            case "regkey":
            case "displayversion":
                return DetectionMethod.RegistryKey;
            case "displayname":
            case "displaynamecontains":
            case "name":
            case "search":
                return DetectionMethod.DisplayNameContains;
            case "path":
            case "folder":
            case "fileorfolder":
            case "fileorfolderexists":
            case "exists":
                return DetectionMethod.FileOrFolderExists;
            default:
                return DetectionMethod.Auto;
        }
    }

    /// <summary>One-line explanation shown in the GUI next to the method buttons.</summary>
    public static string PriorityHint(DetectionMethod method) => method switch
    {
        DetectionMethod.MsiProductCode =>
            "Priority 1 — not exists product \"{GUID}\" of windows installer. Vendor key names cannot change; best for MSI installs.",
        DetectionMethod.FileVersion =>
            "Priority 2 — file \"app.exe\" whose (version of it >= required) of folder \"...\" of program files folder.",
        DetectionMethod.RegistryKey =>
            "Priority 3 — Uninstall key + DisplayVersion compared as version (native registry = correct 32/64-bit view).",
        DetectionMethod.DisplayNameContains =>
            "Priority 4 — fallback when the uninstall key name varies: searches HKLM/HKCU Uninstall (64 + WOW6432Node) by DisplayName.",
        DetectionMethod.FileOrFolderExists =>
            "Priority 5 — simplest check: the install folder / executable exists. No version comparison.",
        _ => "Priority: MSI Product Code → EXE File Version → Registry Key + DisplayVersion → DisplayName Contains → File/Folder Exists."
    };

    public static readonly DetectionMethod[] All =
    {
        DetectionMethod.Auto,
        DetectionMethod.MsiProductCode,
        DetectionMethod.FileVersion,
        DetectionMethod.RegistryKey,
        DetectionMethod.DisplayNameContains,
        DetectionMethod.FileOrFolderExists
    };
}

/// <summary>Inputs required to generate relevance for one application.</summary>
public sealed class DetectionInput
{
    public string Type { get; set; } = FixletTemplates.TypeInstall;
    public DetectionMethod Method { get; set; } = DetectionMethod.Auto;
    public string AppName { get; set; } = "";
    /// <summary>Required (target) version.</summary>
    public string Version { get; set; } = "";
    public string InstallPath { get; set; } = "";
    public string RegistryKeyPath { get; set; } = "";
    public string RegistryValueName { get; set; } = "DisplayVersion";
    public string RegistryValue { get; set; } = "";
    public string MsiProductCode { get; set; } = "";
}

/// <summary>Generated targeting relevance + matching success criteria.</summary>
public sealed class DetectionResult
{
    public DetectionMethod RequestedMethod { get; init; } = DetectionMethod.Auto;
    /// <summary>Method actually used (Auto resolves to a concrete strategy).</summary>
    public DetectionMethod Method { get; set; } = DetectionMethod.Auto;
    public List<string> Relevance { get; set; } = new();
    public string SuccessCriteria { get; set; } = "true";
    public string? Warning { get; set; }
}

public static class RelevanceBuilder
{
    private static readonly string[] UninstallRoots =
    {
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    private static readonly string[] ExecutableExtensions =
        { ".exe", ".dll", ".msi", ".com", ".scr", ".cpl", ".sys", ".bat", ".cmd", ".ps1", ".msix", ".appx" };

    // ─── Public entry point ────────────────────────────────────────────────

    /// <summary>
    /// Build targeting relevance (and its mirror success criteria) for a fixlet/task.
    /// Install / upgrade use the combined "not installed OR older than required" pattern;
    /// uninstall uses "is installed".
    /// </summary>
    public static DetectionResult BuildDetection(DetectionInput input)
    {
        input ??= new DetectionInput();

        // No explicit key supplied: look one up on this machine (same app = same Uninstall key).
        if (string.IsNullOrWhiteSpace(input.RegistryKeyPath) &&
            !string.IsNullOrWhiteSpace(input.AppName) &&
            (input.Method == DetectionMethod.Auto || input.Method == DetectionMethod.RegistryKey))
        {
            var discovered = FindRegistryKey(input.AppName);
            if (discovered.Length > 0)
                input = Clone(input, discovered);
        }

        var result = new DetectionResult { RequestedMethod = input.Method };
        var method = ResolveMethod(input, out var warning);
        result.Method = method;
        result.Warning = warning;

        var (relevance, success) = method switch
        {
            DetectionMethod.MsiProductCode => BuildMsi(input),
            DetectionMethod.FileVersion => BuildFileVersion(input),
            DetectionMethod.RegistryKey => BuildRegistryKey(input),
            DetectionMethod.DisplayNameContains => BuildDisplayNameSearch(input),
            _ => BuildFileOrFolder(input)
        };

        if (relevance.Count == 0)
        {
            relevance.Add("true");
            success = "true";
            result.Warning = "No detection inputs supplied - relevance defaults to 'true' (applies to every computer).";
        }

        result.Relevance = relevance;
        result.SuccessCriteria = success;
        return result;
    }

    // ─── Backward-compatible wrappers ──────────────────────────────────────

    public static List<string> BuildForInstall(string installPath, string appName, string version,
        string registryKeyPath = "", string registryValueName = "DisplayVersion", string registryValue = "",
        DetectionMethod method = DetectionMethod.Auto, string msiProductCode = "")
        => BuildDetection(new DetectionInput
        {
            Type = FixletTemplates.TypeInstall,
            Method = method,
            AppName = appName,
            Version = version,
            InstallPath = installPath,
            RegistryKeyPath = registryKeyPath,
            RegistryValueName = registryValueName,
            RegistryValue = registryValue,
            MsiProductCode = msiProductCode
        }).Relevance;

    public static List<string> BuildForUpgrade(string installPath, string appName, string currentVersion, string targetVersion,
        string registryKeyPath = "", string registryValueName = "DisplayVersion", string registryValue = "",
        DetectionMethod method = DetectionMethod.Auto, string msiProductCode = "")
        => BuildDetection(new DetectionInput
        {
            Type = FixletTemplates.TypeUpgrade,
            Method = method,
            AppName = appName,
            Version = targetVersion,
            InstallPath = installPath,
            RegistryKeyPath = registryKeyPath,
            RegistryValueName = registryValueName,
            RegistryValue = registryValue,
            MsiProductCode = msiProductCode
        }).Relevance;

    public static List<string> BuildForUninstall(string installPath, string appName, string uninstallString,
        string registryKeyPath = "", string registryValueName = "DisplayVersion", string registryValue = "",
        DetectionMethod method = DetectionMethod.Auto, string msiProductCode = "")
        => BuildDetection(new DetectionInput
        {
            Type = FixletTemplates.TypeUninstall,
            Method = method,
            AppName = appName,
            InstallPath = installPath,
            RegistryKeyPath = registryKeyPath,
            RegistryValueName = registryValueName,
            RegistryValue = registryValue,
            MsiProductCode = msiProductCode
        }).Relevance;

    public static string BuildSuccessCriteria(string installPath, string type, string version,
        string registryKeyPath = "", string registryValueName = "DisplayVersion", string registryValue = "",
        DetectionMethod method = DetectionMethod.Auto, string msiProductCode = "")
        => BuildDetection(new DetectionInput
        {
            Type = type,
            Method = method,
            Version = version,
            InstallPath = installPath,
            RegistryKeyPath = registryKeyPath,
            RegistryValueName = registryValueName,
            RegistryValue = registryValue,
            MsiProductCode = msiProductCode
        }).SuccessCriteria;

    /// <summary>
    /// Scanned applications already carry the exact uninstall key, so prefer the registry method
    /// (and the MSI product code when the uninstall key is a product GUID) over path guessing.
    /// </summary>
    public static List<string> BuildFromInstalledApp(InstalledApp app, string type,
        DetectionMethod method = DetectionMethod.Auto, string msiProductCode = "")
    {
        if (app is null) return new List<string> { "true" };

        if (method == DetectionMethod.Auto &&
            string.IsNullOrWhiteSpace(msiProductCode) &&
            string.IsNullOrWhiteSpace(app.MsiProductCode) &&
            !string.IsNullOrWhiteSpace(app.RegistryKeyPath))
        {
            method = DetectionMethod.RegistryKey;
        }

        return BuildDetection(new DetectionInput
        {
            Type = type,
            Method = method,
            AppName = app.Name,
            Version = app.Version,
            InstallPath = GuessInstallPath(app),
            RegistryKeyPath = app.RegistryKeyPath,
            RegistryValueName = string.IsNullOrWhiteSpace(app.RegistryValueName) ? "DisplayVersion" : app.RegistryValueName,
            RegistryValue = app.RegistryValue,
            MsiProductCode = msiProductCode.Length > 0 ? msiProductCode : app.MsiProductCode
        }).Relevance;
    }

    public static string BuildSuccessCriteriaFromInstalledApp(InstalledApp app, string type,
        DetectionMethod method = DetectionMethod.Auto, string msiProductCode = "")
        => BuildDetection(new DetectionInput
        {
            Type = type,
            Method = method,
            AppName = app.Name,
            Version = app.Version,
            InstallPath = GuessInstallPath(app),
            RegistryKeyPath = app.RegistryKeyPath,
            RegistryValueName = string.IsNullOrWhiteSpace(app.RegistryValueName) ? "DisplayVersion" : app.RegistryValueName,
            RegistryValue = app.RegistryValue,
            MsiProductCode = msiProductCode.Length > 0 ? msiProductCode : app.MsiProductCode
        }).SuccessCriteria;

    private static DetectionInput Clone(DetectionInput input, string discoveredKey) => new()
    {
        Type = input.Type,
        Method = input.Method,
        AppName = input.AppName,
        Version = input.Version,
        InstallPath = input.InstallPath,
        RegistryKeyPath = discoveredKey,
        RegistryValueName = input.RegistryValueName,
        RegistryValue = input.RegistryValue,
        MsiProductCode = input.MsiProductCode
    };

    // ─── Method resolution ─────────────────────────────────────────────────

    private static DetectionMethod ResolveMethod(DetectionInput input, out string? warning)
    {
        warning = null;
        var path = (input.InstallPath ?? "").Trim();
        var hasMsi = NormalizeProductCode(input.MsiProductCode).Length > 0;
        var hasKey = !string.IsNullOrWhiteSpace(input.RegistryKeyPath);
        var hasName = !string.IsNullOrWhiteSpace(input.AppName);
        var hasFile = LooksLikeFile(path);

        switch (input.Method)
        {
            case DetectionMethod.MsiProductCode:
                if (hasMsi) return DetectionMethod.MsiProductCode;
                warning = "No MSI product code supplied - falling back to automatic detection.";
                break;
            case DetectionMethod.FileVersion:
                if (path.Length > 0) return DetectionMethod.FileVersion;
                warning = "No install path supplied - falling back to automatic detection.";
                break;
            case DetectionMethod.RegistryKey:
                if (hasKey) return DetectionMethod.RegistryKey;
                warning = "No registry key supplied - falling back to automatic detection.";
                break;
            case DetectionMethod.DisplayNameContains:
                if (hasName) return DetectionMethod.DisplayNameContains;
                warning = "No application name supplied - falling back to automatic detection.";
                break;
            case DetectionMethod.FileOrFolderExists:
                return DetectionMethod.FileOrFolderExists;
        }

        // Automatic priority: MSI → file version → registry key → DisplayName → folder/file.
        if (hasMsi) return DetectionMethod.MsiProductCode;
        if (hasFile) return DetectionMethod.FileVersion;
        if (hasKey) return DetectionMethod.RegistryKey;
        if (hasName) return DetectionMethod.DisplayNameContains;
        return DetectionMethod.FileOrFolderExists;
    }

    // ─── 1. MSI product code ───────────────────────────────────────────────

    private static (List<string> relevance, string success) BuildMsi(DetectionInput input)
    {
        var code = NormalizeProductCode(input.MsiProductCode);
        var relevance = new List<string>();

        if (IsUninstall(input.Type))
        {
            relevance.Add($"exists product \"{code}\" of windows installer");
            return (relevance, $"not exists product \"{code}\" of windows installer");
        }

        var required = RequiredVersion(input);
        if (required.Length > 0 && LooksLikeVersion(required))
        {
            var cond = $"products whose (product code of it as string as uppercase = \"{code}\" and version of it >= \"{required}\" as version) of windows installer";
            relevance.Add($"not exists {cond}");
            return (relevance, $"exists {cond}");
        }

        relevance.Add($"not exists product \"{code}\" of windows installer");
        return (relevance, $"exists product \"{code}\" of windows installer");
    }

    // ─── 2. EXE file version ───────────────────────────────────────────────

    private static (List<string> relevance, string success) BuildFileVersion(DetectionInput input)
    {
        var path = (input.InstallPath ?? "").Trim().Replace('/', '\\');
        var relevance = new List<string>();

        if (LooksLikeFile(path))
        {
            if (IsUninstall(input.Type))
            {
                var plain = FileExpr(path);
                relevance.Add($"exists {plain}");
                return (relevance, $"not exists {plain}");
            }

            var required = RequiredVersion(input);
            if (required.Length > 0 && LooksLikeVersion(required))
            {
                var cond = FileExpr(path, $"version of it >= \"{required}\" as version");
                relevance.Add($"not exists {cond}");
                return (relevance, $"exists {cond}");
            }

            var existsExpr = FileExpr(path);
            relevance.Add($"not exists {existsExpr}");
            return (relevance, $"exists {existsExpr}");
        }

        // Directory path: fall back to folder existence.
        var folderExpr = FolderExpr(path.TrimEnd('\\'));
        if (folderExpr.Length == 0)
            return BuildFileOrFolder(input);

        if (IsUninstall(input.Type))
        {
            relevance.Add($"exists {folderExpr}");
            return (relevance, $"not exists {folderExpr}");
        }

        relevance.Add($"not exists {folderExpr}");
        return (relevance, $"exists {folderExpr}");
    }

    // ─── 3. Registry uninstall key + DisplayVersion ────────────────────────

    private static (List<string> relevance, string success) BuildRegistryKey(DetectionInput input)
    {
        SplitRegKey(input.RegistryKeyPath ?? "", out var parent, out var leaf);
        var valueName = string.IsNullOrWhiteSpace(input.RegistryValueName)
            ? "DisplayVersion"
            : input.RegistryValueName.Trim();
        var required = RequiredVersion(input);
        var relevance = new List<string>();

        if (leaf.Length == 0)
        {
            if (IsUninstall(input.Type))
            {
                relevance.Add($"exists key \"{parent}\" of native registry");
                return (relevance, $"not exists key \"{parent}\" of native registry");
            }

            relevance.Add($"not exists key \"{parent}\" of native registry");
            return (relevance, $"exists key \"{parent}\" of native registry");
        }

        string? valueCond = null;
        if (required.Length > 0 && LooksLikeVersion(required))
            valueCond = $"exists value \"{valueName}\" of it and value \"{valueName}\" of it as string as version >= \"{required}\" as version";
        else if (required.Length > 0)
            valueCond = $"exists value \"{valueName}\" of it and value \"{valueName}\" of it as string = \"{Escape(required)}\"";

        var existsExpr = valueCond is null
            ? $"exists keys \"{leaf}\" of keys \"{parent}\" of native registry"
            : $"exists keys \"{leaf}\" whose ({valueCond}) of keys \"{parent}\" of native registry";
        var notExistsExpr = valueCond is null
            ? $"not exists keys \"{leaf}\" of keys \"{parent}\" of native registry"
            : $"not exists keys \"{leaf}\" whose ({valueCond}) of keys \"{parent}\" of native registry";

        if (IsUninstall(input.Type))
        {
            relevance.Add(existsExpr);
            return (relevance, notExistsExpr);
        }

        relevance.Add(notExistsExpr);
        return (relevance, existsExpr);
    }

    // ─── 4. DisplayName contains (fallback) ─────────────────────────────────

    private static (List<string> relevance, string success) BuildDisplayNameSearch(DetectionInput input)
    {
        var name = Escape(DisplayNameSearchTerm(input.AppName).ToLowerInvariant());
        var required = RequiredVersion(input);
        var relevance = new List<string>();

        var nameCond = $"exists value \"DisplayName\" of it and value \"DisplayName\" of it as string as lowercase contains \"{name}\"";

        string installedCond() => nameCond;

        string compliantCond()
        {
            var cond = nameCond;
            if (required.Length > 0 && LooksLikeVersion(required))
                cond += $" and exists value \"DisplayVersion\" of it and value \"DisplayVersion\" of it as string as version >= \"{required}\" as version";
            else if (required.Length > 0)
                cond += $" and exists value \"DisplayVersion\" of it and value \"DisplayVersion\" of it as string = \"{Escape(required)}\"";
            return cond;
        }

        string Exists(string cond, string root) =>
            $"exists keys whose ({cond}) of keys \"{root}\" of native registry";

        string NotExists(string cond, string root) =>
            $"not exists keys whose ({cond}) of keys \"{root}\" of native registry";

        if (IsUninstall(input.Type))
        {
            // One line per hive would AND the checks - the app only lives in one of them, so OR them.
            relevance.Add(string.Join(" or ", UninstallRoots.Select(r => Exists(installedCond(), r))));
            return (relevance, string.Join(" AND ", UninstallRoots.Select(r => NotExists(installedCond(), r))));
        }

        // "Not installed anywhere OR installed below the required version" - must hold in every hive.
        var cond = compliantCond();
        foreach (var root in UninstallRoots)
            relevance.Add(NotExists(cond, root));

        return (relevance, string.Join(" or ", UninstallRoots.Select(r => Exists(cond, r))));
    }

    // ─── 5. File / folder exists ───────────────────────────────────────────

    private static (List<string> relevance, string success) BuildFileOrFolder(DetectionInput input)
    {
        var path = (input.InstallPath ?? "").Trim().Replace('/', '\\').TrimEnd('\\');
        var relevance = new List<string>();

        if (path.Length == 0)
        {
            var guess = $"folder \"{GuessInstallFolder(input.AppName)}\"";
            if (IsUninstall(input.Type))
            {
                relevance.Add($"exists {guess}");
                return (relevance, $"not exists {guess}");
            }
            relevance.Add($"not exists {guess}");
            return (relevance, $"exists {guess}");
        }

        var expr = LooksLikeFile(path) ? FileExpr(path) : FolderExpr(path);
        if (expr.Length == 0) expr = $"file \"{path}\"";

        if (IsUninstall(input.Type))
        {
            relevance.Add($"exists {expr}");
            return (relevance, $"not exists {expr}");
        }

        relevance.Add($"not exists {expr}");
        return (relevance, $"exists {expr}");
    }

    // ─── Path → BigFix expression helpers ──────────────────────────────────

    /// <summary>
    /// e.g. C:\Program Files\7-Zip\7z.exe → file "7z.exe" of folder "7-Zip" of program files folder
    /// with an optional whose() clause directly after the file name:
    /// file "7z.exe" whose (version of it >= "25.00" as version) of folder "7-Zip" of program files folder
    /// </summary>
    private static string FileExpr(string path, string whoseClause = "")
    {
        path = path.Replace('/', '\\').TrimEnd('\\');
        var name = System.IO.Path.GetFileName(path);
        var dir = System.IO.Path.GetDirectoryName(path) ?? "";

        if (name.Length == 0)
            return FolderExpr(path);

        var whose = string.IsNullOrWhiteSpace(whoseClause) ? "" : $" whose ({whoseClause})";
        var location = FolderExpr(dir);
        return location.Length == 0
            ? $"file \"{name}\"{whose}"
            : $"file \"{name}\"{whose} of {location}";
    }

    /// <summary>
    /// Turns a directory into a BigFix folder expression using well-known roots:
    /// Program Files, Program Files (x86), ProgramData, AppData\Local / Roaming.
    /// </summary>
    private static string FolderExpr(string dir)
    {
        dir = (dir ?? "").Trim().Replace('/', '\\').TrimEnd('\\');
        if (dir.Length == 0) return "";

        var localIdx = dir.IndexOf(@"\AppData\Local\", StringComparison.OrdinalIgnoreCase);
        if (localIdx >= 0)
        {
            var rel = dir[(localIdx + @"\AppData\Local\".Length)..];
            return rel.Length == 0
                ? "local appdata folder of current user"
                : $"folder \"{rel}\" of local appdata folder of current user";
        }

        var roamIdx = dir.IndexOf(@"\AppData\Roaming\", StringComparison.OrdinalIgnoreCase);
        if (roamIdx >= 0)
        {
            var rel = dir[(roamIdx + @"\AppData\Roaming\".Length)..];
            return rel.Length == 0
                ? "appdata folder of current user"
                : $"folder \"{rel}\" of appdata folder of current user";
        }

        if (TryRootedSub(dir, "Program Files (x86)", out var rel86))
            return rel86.Length == 0
                ? "program files (x86) folder"
                : $"folder \"{rel86}\" of program files (x86) folder";

        if (TryRootedSub(dir, "Program Files", out var relPf))
            return relPf.Length == 0
                ? "program files folder"
                : $"folder \"{relPf}\" of program files folder";

        if (TryRootedSub(dir, "ProgramData", out var relPd))
            return relPd.Length == 0
                ? "common appdata folder"
                : $"folder \"{relPd}\" of common appdata folder";

        if (!System.IO.Path.IsPathRooted(dir))
            return $"folder \"{dir}\" of program files folder";

        return $"folder \"{dir}\"";
    }

    /// <summary>C:\Program Files\7-Zip → ("7-Zip") when "Program Files" sits directly under the drive root.</summary>
    private static bool TryRootedSub(string dir, string rootName, out string relative)
    {
        relative = "";
        var marker = "\\" + rootName + "\\";
        var withSlash = dir.EndsWith("\\") ? dir : dir + "\\";
        var idx = withSlash.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx != 2) return false;
        relative = withSlash[(idx + marker.Length)..].TrimEnd('\\');
        return true;
    }

    // ─── Shared helpers ────────────────────────────────────────────────────

    private static bool IsUninstall(string type) =>
        string.Equals(type, FixletTemplates.TypeUninstall, StringComparison.OrdinalIgnoreCase);

    private static string RequiredVersion(DetectionInput input)
    {
        var value = !string.IsNullOrWhiteSpace(input.RegistryValue) ? input.RegistryValue : input.Version;
        return (value ?? "").Trim();
    }

    private static bool LooksLikeFile(string path)
    {
        var p = (path ?? "").Trim().Replace('/', '\\').TrimEnd('\\');
        if (p.Length == 0) return false;

        var ext = System.IO.Path.GetExtension(p);
        if (ext.Length == 0) return false;
        if (ExecutableExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return true;
        return ext.Length <= 5;
    }

    private static string NormalizeProductCode(string code)
    {
        var c = (code ?? "").Trim();
        if (c.Length == 0) return "";
        if (!c.StartsWith('{')) c = "{" + c.TrimStart('{');
        if (!c.EndsWith('}')) c = c + "}";
        return c.ToUpperInvariant();
    }

    private static string Escape(string value) => (value ?? "").Replace("\"", "");

    /// <summary>
    /// Uninstall keys often carry a version in DisplayName ("7-Zip 26.01 (x64)"). Strip the trailing
    /// version/architecture token so the contains-search still matches machines with an older build;
    /// the DisplayVersion comparison decides whether they are outdated.
    /// </summary>
    private static string DisplayNameSearchTerm(string appName)
    {
        var name = (appName ?? "").Trim();
        if (name.Length == 0) return "";

        var stripped = System.Text.RegularExpressions.Regex.Replace(
            name, @"\s*\((x64|x86|amd64|arm64|64-?bit|32-?bit|win64|win32)\)$", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        stripped = System.Text.RegularExpressions.Regex.Replace(
            stripped, @"[\s._-]+[0-9]+([._][0-9]+)*(?:\s*\([0-9.]+\))?$", "");

        return stripped.Trim().Length >= 3 ? stripped.Trim() : name;
    }

    private static bool LooksLikeVersion(string s) =>
        !string.IsNullOrWhiteSpace(s) && s.All(c => char.IsDigit(c) || c == '.' || c == ' ');

    /// <summary>
    /// Split a full registry path into parent key + leaf subkey name.
    /// e.g. HKLM\SOFTWARE\...\Uninstall\{GUID} → parent=HKLM\SOFTWARE\...\Uninstall, leaf={GUID}
    /// </summary>
    private static void SplitRegKey(string fullPath, out string parent, out string leaf)
    {
        var p = (fullPath ?? "")
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

    private static string FindRegistryKey(string appName)
    {
        try
        {
            var baseKeys = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var basePath in baseKeys)
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(basePath);
                if (key is null) continue;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey is null) continue;

                    var displayName = subKey.GetValue("DisplayName") as string ?? "";
                    if (displayName.Contains(appName, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"HKLM\\{basePath}\\{subKeyName}";
                    }
                }
            }

            foreach (var basePath in baseKeys)
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(basePath);
                if (key is null) continue;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey is null) continue;

                    var displayName = subKey.GetValue("DisplayName") as string ?? "";
                    if (displayName.Contains(appName, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"HKCU\\{basePath}\\{subKeyName}";
                    }
                }
            }
        }
        catch
        {
            // ignore
        }
        return "";
    }

    private static string GuessInstallPath(InstalledApp app)
    {
        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && System.IO.Directory.Exists(app.InstallLocation))
        {
            var exe = System.IO.Directory.GetFiles(app.InstallLocation, "*.exe", System.IO.SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (exe is not null) return exe;
            return System.IO.Path.Combine(app.InstallLocation, app.Name + ".exe");
        }

        if (!string.IsNullOrWhiteSpace(app.InstallLocation))
            return System.IO.Path.Combine(app.InstallLocation, app.Name + ".exe");

        var commonPaths = new[]
        {
            $@"C:\Program Files\{app.Name}\{app.Name}.exe",
            $@"C:\Program Files (x86)\{app.Name}\{app.Name}.exe",
            $@"C:\Program Files\{app.Publisher}\{app.Name}\{app.Name}.exe",
            $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\{app.Name}\{app.Name}.exe"
        };

        return commonPaths.FirstOrDefault(p => System.IO.File.Exists(p))
            ?? commonPaths[0];
    }

    private static string GuessInstallFolder(string appName)
    {
        var paths = new[]
        {
            $@"C:\Program Files\{appName}",
            $@"C:\Program Files (x86)\{appName}",
            $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\{appName}"
        };

        return paths.FirstOrDefault(p => System.IO.Directory.Exists(p)) ?? paths[0];
    }
}
