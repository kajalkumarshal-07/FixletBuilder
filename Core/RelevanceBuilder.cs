namespace FixletBuilder.Core;

public static class RelevanceBuilder
{
    public static List<string> BuildForInstall(string installPath, string appName, string version,
        string registryKeyPath = "", string registryValueName = "DisplayVersion", string registryValue = "")
    {
        var relevance = new List<string>();

        if (!string.IsNullOrWhiteSpace(registryKeyPath))
        {
            if (!string.IsNullOrWhiteSpace(registryValue) && !string.IsNullOrWhiteSpace(version))
            {
                relevance.Add($"not (exists value \"{registryValueName}\" of key \"{registryKeyPath}\" of native registry AND value \"{registryValueName}\" of key \"{registryKeyPath}\" of native registry as string = \"{registryValue}\")");
            }
            else if (!string.IsNullOrWhiteSpace(registryValue))
            {
                relevance.Add($"not (exists value \"{registryValueName}\" of key \"{registryKeyPath}\" of native registry AND value \"{registryValueName}\" of key \"{registryKeyPath}\" of native registry as string = \"{registryValue}\")");
            }
            else
            {
                relevance.Add($"not exists key \"{registryKeyPath}\" of native registry");
            }
        }
        else
        {
            var foundKey = FindRegistryKey(appName);
            if (foundKey.Length > 0)
            {
                relevance.Add($"not (exists value \"{registryValueName}\" of key \"{foundKey}\" of native registry AND value \"{registryValueName}\" of key \"{foundKey}\" of native registry as string = \"{version}\")");
            }
            else if (!string.IsNullOrWhiteSpace(installPath))
            {
                relevance.Add($"not exists file \"{installPath}\"");
            }
            else
            {
                relevance.Add($"not exists folder \"{GuessInstallFolder(appName)}\"");
            }
        }

        return relevance;
    }

    public static List<string> BuildForUpgrade(string installPath, string appName, string currentVersion, string targetVersion,
        string registryKeyPath = "", string registryValueName = "DisplayVersion", string registryValue = "")
    {
        var relevance = new List<string>();

        if (!string.IsNullOrWhiteSpace(registryKeyPath))
        {
            if (!string.IsNullOrWhiteSpace(registryValueName) && !string.IsNullOrWhiteSpace(targetVersion))
            {
                relevance.Add($"exists value \"{registryValueName}\" of key \"{registryKeyPath}\" of native registry");
                relevance.Add($"value \"{registryValueName}\" of key \"{registryKeyPath}\" of native registry as string as version < \"{targetVersion}\" as version");
            }
            else if (!string.IsNullOrWhiteSpace(registryValueName))
            {
                relevance.Add($"exists value \"{registryValueName}\" of key \"{registryKeyPath}\" of native registry");
            }
            else
            {
                relevance.Add($"exists key \"{registryKeyPath}\" of native registry");
            }
        }
        else
        {
            var foundKey = FindRegistryKey(appName);
            if (foundKey.Length > 0)
            {
                relevance.Add($"exists value \"{registryValueName}\" of key \"{foundKey}\" of native registry");
                if (!string.IsNullOrWhiteSpace(targetVersion))
                {
                    relevance.Add($"value \"{registryValueName}\" of key \"{foundKey}\" of native registry as string as version < \"{targetVersion}\" as version");
                }
            }
            else if (!string.IsNullOrWhiteSpace(installPath))
            {
                relevance.Add($"exists file \"{installPath}\"");
                if (!string.IsNullOrWhiteSpace(targetVersion))
                {
                    relevance.Add($"version of file \"{installPath}\" < \"{targetVersion}\" as version");
                }
            }
            else
            {
                relevance.Add($"exists folder \"{GuessInstallFolder(appName)}\"");
            }
        }

        return relevance;
    }

    public static List<string> BuildForUninstall(string installPath, string appName, string uninstallString,
        string registryKeyPath = "", string registryValueName = "DisplayVersion", string registryValue = "")
    {
        var relevance = new List<string>();

        if (!string.IsNullOrWhiteSpace(registryKeyPath))
        {
            relevance.Add($"exists key \"{registryKeyPath}\" of native registry");
        }
        else
        {
            var foundKey = FindRegistryKey(appName);
            if (foundKey.Length > 0)
            {
                relevance.Add($"exists key \"{foundKey}\" of native registry");
            }
            else if (!string.IsNullOrWhiteSpace(installPath))
            {
                relevance.Add($"exists file \"{installPath}\"");
            }
            else
            {
                relevance.Add($"exists folder \"{GuessInstallFolder(appName)}\"");
            }
        }

        return relevance;
    }

    public static List<string> BuildFromInstalledApp(InstalledApp app, string type)
    {
        return type.ToLowerInvariant() switch
        {
            "upgrade" => BuildForUpgrade(
                GuessInstallPath(app),
                app.Name,
                app.Version,
                "",
                app.RegistryKeyPath,
                app.RegistryValueName,
                app.RegistryValue),
            "uninstall" => BuildForUninstall(
                GuessInstallPath(app),
                app.Name,
                app.UninstallString,
                app.RegistryKeyPath,
                app.RegistryValueName,
                app.RegistryValue),
            _ => BuildForInstall(
                GuessInstallPath(app),
                app.Name,
                app.Version,
                app.RegistryKeyPath,
                app.RegistryValueName,
                app.RegistryValue)
        };
    }

    public static string BuildSuccessCriteria(string installPath, string type, string version,
        string registryKeyPath = "", string registryValueName = "DisplayVersion")
    {
        var criteria = new List<string>();

        switch (type.ToLowerInvariant())
        {
            case "upgrade":
                if (!string.IsNullOrWhiteSpace(registryKeyPath))
                {
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        criteria.Add($"exists value \"{registryValueName}\" of key \"{registryKeyPath}\" of native registry");
                        criteria.Add($"value \"{registryValueName}\" of key \"{registryKeyPath}\" of native registry as string as version >= \"{version}\" as version");
                    }
                    else
                    {
                        criteria.Add($"exists key \"{registryKeyPath}\" of native registry");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(installPath))
                {
                    if (!string.IsNullOrWhiteSpace(version))
                        criteria.Add($"version of file \"{installPath}\" >= \"{version}\" as version");
                    else
                        criteria.Add($"exists file \"{installPath}\"");
                }
                break;

            case "uninstall":
                if (!string.IsNullOrWhiteSpace(registryKeyPath))
                    criteria.Add($"not exists key \"{registryKeyPath}\" of native registry");
                else if (!string.IsNullOrWhiteSpace(installPath))
                    criteria.Add($"not exists file \"{installPath}\"");
                break;

            default: // install
                if (!string.IsNullOrWhiteSpace(registryKeyPath))
                    criteria.Add($"exists key \"{registryKeyPath}\" of native registry");
                else if (!string.IsNullOrWhiteSpace(installPath))
                    criteria.Add($"exists file \"{installPath}\"");
                break;
        }

        if (criteria.Count == 0)
        {
            return "true";
        }

        return string.Join(" AND ", criteria);
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
