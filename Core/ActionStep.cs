namespace FixletBuilder.Core;

/// <summary>
/// A single step in the button-driven action script builder.
/// Script text is re-rendered from Kind + Params whenever form fields change.
/// </summary>
public class ActionStep
{
    public string Kind { get; set; } = "";
    public Dictionary<string, string> Params { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string Label => Kind switch
    {
        "download" => "Download (prefetch)",
        "kill" => $"Kill process: {Get("process")}",
        "stop" => $"Stop service: {Get("service")}",
        "deleteFile" => $"Delete file: {Get("path")}",
        "deleteFolder" => $"Delete folder: {Get("path")}",
        "copy" => $"Copy: {Get("src")} → {Get("dst")}",
        "move" => $"Move: {Get("src")} → {Get("dst")}",
        "mkdir" => $"Create folder: {Get("path")}",
        "regset" => $"Set registry: {Get("key")}",
        "regset64" => $"Set registry (64): {Get("key")}",
        "regdelete" => $"Delete reg value: {Get("name")}",
        "regkeydelete" => $"Delete reg key: {Get("key")}",
        "install" => $"Install ({(Get("mode") == "exe" ? "EXE" : "MSI")}) silent",
        "uninstall" => "Run uninstall",
        "run" => $"Run: {Get("cmd")}",
        "wait" => $"Wait: {Get("cmd")}",
        "restart" => $"Restart (delay {Get("delay")}s)",
        "shutdown" => $"Shutdown (delay {Get("delay")}s)",
        "actionRestart" => "Action requires restart",
        "clientRestart" => "Client restart",
        "forceRefresh" => "Force refresh",
        "continueIf" => $"Continue if {{...}}",
        "pauseWhile" => $"Pause while {{...}}",
        "createConfig" => $"Create config: {Get("file")}",
        "appendFile" => $"Append to: {Get("file")}",
        "custom" => $"Custom: {Get("line")}",
        _ => Kind
    };

    public string Get(string key) =>
        Params.TryGetValue(key, out var v) ? v : "";

    public string Render(FormSnapshot f)
    {
        switch (Kind)
        {
            case "download":
                return ActionScriptCommands.Snippets.Prefetch(
                    string.IsNullOrWhiteSpace(Get("file"))
                        ? ActionScriptCommands.DeriveDownloadFileName(f.Url, f.AppName)
                        : Get("file"),
                    f.Sha1, f.Sha256, f.FileSize, f.Url);

            case "kill":
                return ActionScriptCommands.Snippets.KillProcess(Get("process"));

            case "stop":
                return ActionScriptCommands.Snippets.StopService(Get("service"));

            case "deleteFile":
                return ActionScriptCommands.Snippets.DeleteFile(Get("path"));

            case "deleteFolder":
                return ActionScriptCommands.Snippets.DeleteFolder(Get("path"));

            case "copy":
                return ActionScriptCommands.Snippets.CopyFile(Get("src"), Get("dst"));

            case "move":
                return ActionScriptCommands.Snippets.MoveFile(Get("src"), Get("dst"));

            case "mkdir":
                return ActionScriptCommands.Snippets.CreateFolder(Get("path"));

            case "regset":
                return ActionScriptCommands.Snippets.SetRegistry(Get("key"), Get("name"), Get("value"));

            case "regset64":
                return ActionScriptCommands.Snippets.SetRegistry64(Get("key"), Get("name"), Get("value"));

            case "regdelete":
                return ActionScriptCommands.Snippets.DeleteRegistry(Get("key"), Get("name"));

            case "regkeydelete":
                return ActionScriptCommands.Snippets.DeleteRegistryKey(Get("key"));

            case "install":
            {
                var silent = string.IsNullOrWhiteSpace(Get("args")) ? f.SilentArgs : Get("args}");
                var url = string.IsNullOrWhiteSpace(Get("url")) ? f.Url : Get("url");
                var file = string.IsNullOrWhiteSpace(Get("file"))
                    ? ActionScriptCommands.DeriveDownloadFileName(url, f.AppName)
                    : Get("file");
                var local = string.IsNullOrWhiteSpace(Get("path")) ? f.InstallPath : Get("path");

                if (!string.IsNullOrWhiteSpace(url))
                    return ActionScriptCommands.Snippets.InstallDownloaded(file, silent, url);
                return ActionScriptCommands.Snippets.InstallLocal(local, silent);
            }

            case "uninstall":
            {
                var cmd = string.IsNullOrWhiteSpace(Get("cmd")) ? f.UninstallString : Get("cmd");
                var args = Get("args");
                return string.IsNullOrWhiteSpace(cmd)
                    ? "// uninstall: UninstallString missing - fill the form field"
                    : ActionScriptCommands.Snippets.BuildRunCommand(cmd, args);
            }

            case "run":
            case "wait":
            {
                var cmd = Get("cmd");
                return Kind == "wait" ? $"wait {cmd}" : $"waithidden {cmd}";
            }

            case "restart":
                return ActionScriptCommands.Snippets.RestartComputer(int.TryParse(Get("delay"), out var d) ? d : 0);

            case "shutdown":
                return ActionScriptCommands.Snippets.ShutdownComputer(int.TryParse(Get("delay"), out var s) ? s : 0);

            case "actionRestart":
                return ActionScriptCommands.Snippets.ActionRequiresRestart();

            case "clientRestart":
                return ActionScriptCommands.Snippets.ClientRestart();

            case "forceRefresh":
                return ActionScriptCommands.Snippets.ForceRefresh();

            case "continueIf":
                return ActionScriptCommands.Snippets.ContinueIfRelevant(Get("relevance"));

            case "pauseWhile":
                return ActionScriptCommands.Snippets.PauseWhile(Get("relevance"));

            case "createConfig":
                return ActionScriptCommands.Snippets.CreateConfigFile(Get("file"), Get("content"));

            case "appendFile":
                return ActionScriptCommands.Snippets.AppendToFile(Get("file"), Get("line"));

            case "custom":
                return Get("line");

            default:
                return $"// unknown step: {Kind}";
        }
    }

    public static ActionStep Create(string kind, params (string Key, string Value)[] args)
    {
        var step = new ActionStep { Kind = kind };
        foreach (var (k, v) in args)
            step.Params[k] = v;
        return step;
    }
}

/// <summary>Snapshot of the main form fields used to re-render steps.</summary>
public sealed class FormSnapshot
{
    public string AppName { get; init; } = "";
    public string Url { get; init; } = "";
    public string Sha1 { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string FileSize { get; init; } = "";
    public string SilentArgs { get; init; } = "";
    public string InstallPath { get; init; } = "";
    public string UninstallString { get; init; } = "";
}
