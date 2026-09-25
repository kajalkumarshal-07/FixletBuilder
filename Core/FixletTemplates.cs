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

        r.ActionScript = BuildActionScript(type, v);

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

    private static string BuildActionScript(string type, Dictionary<string, string> v)
    {
        v.TryGetValue("SourceURL", out var url);
        v.TryGetValue("Sha1", out var sha1);
        v.TryGetValue("Sha256", out var sha256);
        v.TryGetValue("FileSize", out var fileSize);
        v.TryGetValue("SilentArgs", out var silentArgs);
        v.TryGetValue("UninstallString", out var uninstallString);
        v.TryGetValue("ProcessToKill", out var processToKill);
        v.TryGetValue("ServiceToStop", out var serviceToStop);
        v.TryGetValue("AppName", out var appName);

        var lines = new List<string>();

        var fileName = ActionScriptCommands.DeriveDownloadFileName(url, appName);

        // Download step (only when URL + sha1 present)
        if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(sha1))
        {
            var size = string.IsNullOrWhiteSpace(fileSize) || fileSize == "0" ? "" : fileSize;
            var prefetch = $"prefetch {fileName} sha1:{sha1}";
            if (!string.IsNullOrWhiteSpace(size))
                prefetch += $" size:{size}";
            prefetch += $" {url}";
            if (!string.IsNullOrWhiteSpace(sha256))
                prefetch += $" sha256:{sha256}";
            lines.Add(prefetch);
        }

        // Pre-install steps
        if (!string.IsNullOrWhiteSpace(processToKill))
            lines.Add(ActionScriptCommands.Snippets.KillProcess(processToKill));
        if (!string.IsNullOrWhiteSpace(serviceToStop))
            lines.Add(ActionScriptCommands.Snippets.StopService(serviceToStop));

        // Main command
        switch (type)
        {
            case TypeUninstall:
                if (!string.IsNullOrWhiteSpace(uninstallString))
                {
                    var args = silentArgs;
                    if (string.IsNullOrWhiteSpace(args))
                        args = ActionScriptCommands.Snippets.SilentArgsForUninstall(uninstallString);
                    lines.Add(ActionScriptCommands.Snippets.BuildRunCommand(uninstallString, args));
                }
                break;

            case TypeCustom:
                lines.Add("// Add your custom action logic here");
                break;

            default: // install / upgrade
                v.TryGetValue("InstallPath", out var ip);
                silentArgs ??= "";
                if (!string.IsNullOrWhiteSpace(url))
                {
                    lines.Add(ActionScriptCommands.Snippets.InstallDownloaded(fileName, silentArgs, url));
                }
                else if (!string.IsNullOrWhiteSpace(ip))
                {
                    lines.Add(ActionScriptCommands.Snippets.InstallLocal(ip, silentArgs));
                }
                else
                {
                    lines.Add($"// TODO: add install command for {appName}");
                }
                break;
        }

        lines.Add("action requires restart");
        return string.Join(Environment.NewLine, lines);
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
