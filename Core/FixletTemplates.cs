namespace FixletBuilder.Core;

public class TemplateData
{
    public string Title { get; set; } = "";
    public string ActionDescription { get; set; } = "";
    public List<string> Relevance { get; set; } = new();
    public string Description { get; set; } = "";
    public string ActionScript { get; set; } = "";
    public string SuccessCriteria { get; set; } = "";
}

public static class FixletTemplates
{
    public const string TypeInstall = "install";
    public const string TypeUpgrade = "upgrade";
    public const string TypeUninstall = "uninstall";
    public const string TypeCustom = "custom";

    public static readonly string[] Types = { TypeInstall, TypeUpgrade, TypeUninstall, TypeCustom };

    private static string Fill(string text, Dictionary<string, string> v)
    {
        foreach (var kv in v)
            text = text.Replace("{" + kv.Key + "}", kv.Value);
        return text;
    }

    public static TemplateData Apply(string type, Dictionary<string, string> v)
    {
        var r = new TemplateData();

        switch (type)
        {
            case TypeUpgrade:
                r.Title = "Upgrade {AppName} to {Version}";
                r.ActionDescription = "Upgrade";
                r.Relevance = new List<string>
                {
                    "exists file \"{InstallPath}\"",
                    "version of file \"{InstallPath}\" < \"{Version}\""
                };
                r.Description = "<h2>Upgrade {AppName} to {Version}</h2><p>This fixlet upgrades {AppName} to version {Version}.</p>";
                r.SuccessCriteria = "version of file \"{InstallPath}\" >= \"{Version}\"";
                break;

            case TypeUninstall:
                r.Title = "Uninstall {AppName}";
                r.ActionDescription = "Uninstall";
                r.Relevance = new List<string> { "exists file \"{InstallPath}\"" };
                r.Description = "<h2>Uninstall {AppName}</h2><p>This fixlet removes {AppName} from the computer.</p>";
                r.SuccessCriteria = "not exists file \"{InstallPath}\"";
                break;

            case TypeCustom:
                r.Title = "{AppName}";
                r.ActionDescription = "Run custom action";
                r.Relevance = new List<string>();
                r.Description = "<h2>{AppName}</h2><p>Custom action. Edit the relevance and script as needed.</p>";
                r.SuccessCriteria = "";
                break;

            default: // Install
                r.Title = "Install {AppName} {Version}";
                r.ActionDescription = "Install";
                r.Relevance = new List<string> { "not exists file \"{InstallPath}\"" };
                r.Description = "<h2>Install {AppName} {Version}</h2><p>This fixlet installs {AppName} version {Version} on the computer.</p>";
                r.SuccessCriteria = "exists file \"{InstallPath}\"";
                break;
        }

        switch (type)
        {
            case TypeInstall:
                r.ActionScript = BuildInstallActionScript();
                break;

            case TypeUpgrade:
                r.ActionScript = BuildUpgradeActionScript();
                break;

            case TypeUninstall:
                r.ActionScript = BuildUninstallActionScript();
                break;

            default:
                r.ActionScript = BuildCustomActionScript();
                break;
        }

        var filled = new TemplateData
        {
            Title = Fill(r.Title, v),
            ActionDescription = r.ActionDescription,
            Relevance = r.Relevance.Select(x => Fill(x, v)).ToList(),
            Description = Fill(r.Description, v),
            ActionScript = Fill(r.ActionScript, v),
            SuccessCriteria = Fill(r.SuccessCriteria, v)
        };
        return filled;
    }

    private static string BuildInstallActionScript()
    {
        return """
            prefetch "{AppName}" sha1:{Sha1} size:{FileSize} "{SourceURL}" sha256:{Sha256}
            waithidden powershell -Command "Stop-Process -Name '{ProcessToKill}' -Force -ErrorAction SilentlyContinue"
            waithidden sc stop "{ServiceToStop}"
            waithidden msiexec /i "{AppName}" /q
            action requires restart
            """;
    }

    private static string BuildUpgradeActionScript()
    {
        return """
            prefetch "{AppName}" sha1:{Sha1} size:{FileSize} "{SourceURL}" sha256:{Sha256}
            waithidden powershell -Command "Stop-Process -Name '{ProcessToKill}' -Force -ErrorAction SilentlyContinue"
            waithidden sc stop "{ServiceToStop}"
            waithidden msiexec /i "{AppName}" /q
            action requires restart
            """;
    }

    private static string BuildUninstallActionScript()
    {
        return """
            waithidden powershell -Command "Stop-Process -Name '{ProcessToKill}' -Force -ErrorAction SilentlyContinue"
            waithidden sc stop "{ServiceToStop}"
            waithidden {UninstallString}
            action requires restart
            """;
    }

    private static string BuildCustomActionScript()
    {
        return """
            // Add your custom action logic here
            """;
    }

    public static TemplateData Apply(string type, string appName, string version, string sourceUrl,
        string silentArgs, string installPath, string uninstallString,
        string sha1 = "", string sha256 = "", long fileSizeBytes = 0,
        string processToKill = "", string serviceToStop = "")
    {
        string actionLabel = type == TypeUninstall ? "Uninstall" : type == TypeUpgrade ? "Upgrade" : type == TypeCustom ? "Custom action" : "Install";

        var vars = new Dictionary<string, string>
        {
            ["AppName"] = appName,
            ["Version"] = version,
            ["SourceURL"] = sourceUrl,
            ["SilentArgs"] = silentArgs,
            ["InstallPath"] = installPath,
            ["UninstallString"] = uninstallString,
            ["Action"] = actionLabel,
            ["Sha1"] = sha1,
            ["Sha256"] = sha256,
            ["FileSize"] = fileSizeBytes.ToString(),
            ["ProcessToKill"] = processToKill,
            ["ServiceToStop"] = serviceToStop
        };

        return Apply(type, vars);
    }
}
