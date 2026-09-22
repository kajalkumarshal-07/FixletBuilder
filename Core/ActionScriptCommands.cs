namespace FixletBuilder.Core;

public static class ActionScriptCommands
{
    public record CommandInfo(
        string Name,
        string Category,
        string Syntax,
        string Description,
        string Example,
        string Platforms,
        string Version = "",
        string Notes = "");

    public static readonly List<CommandInfo> All = new()
    {
        // ═══════════════════════════════════════════════════════
        // DOWNLOAD COMMANDS
        // ═══════════════════════════════════════════════════════
        new("prefetch", "Download",
            "prefetch <name> sha1:<sha1> size:<size> <url> [sha256:<sha256>]",
            "Download file before action begins. Preferred over download. Name must be <=32 chars ASCII only.",
            "prefetch hodor.jpg sha1:ce842e0af799f2ba476511c8fbfdc3bf89612dd0 size:57656 http://i.imgur.com/YAUeUOG.jpg sha256:74f69205a016a3896290eae03627e15e8dfeba812a631b5e0afca140722a322b",
            "All", "8.0.584.0",
            "At least sha1 OR sha256 required. Name: a-z, A-Z, 0-9, -, _, non-leading periods. Do not launch long-run programs from __Download."),

        new("download", "Download",
            "download <url>",
            "Download file from URL. Saved in __Download folder. Fails action on failure.",
            "download http://download.bigfix.com/update/bfxxxx.exe",
            "All", "8.0.584.0",
            "Relevance substitution is NOT performed on download command lines."),

        new("download as", "Download",
            "download as <name> <url>",
            "Download and rename file. Name must be <=32 chars, ASCII only.",
            "download as myprog.exe http://www.website.com/update/prog555.exe",
            "All", "8.0.584.0",
            "Name must be <=32 chars. Only a-z, A-Z, 0-9, -, _, non-leading periods. Relevance substitution NOT allowed."),

        new("download open", "Download",
            "download open <url>",
            "Download and run ShellExecute on the resulting file.",
            "download open http://download.bigfix.com/update/bfxxxx.exe",
            "Windows", "8.0.584.0",
            "Relevance substitution NOT allowed. Uses Windows ShellExecute API."),

        new("begin prefetch block", "Download",
            "begin prefetch block",
            "Start prefetch block for batch downloads. Only one block per action.",
            "begin prefetch block\nadd prefetch item name=app.exe sha1:abc size:1024 url:\"http://server/app.exe\"\nend prefetch block",
            "All", "8.0.584.0",
            "Only comments/blank lines before it. When used, download/download as/prefetch NOT allowed anywhere in script."),

        new("add prefetch item", "Download",
            "add prefetch item [name=<name>] [sha1=<sha1>] [sha256=<sha256>] size=<size> url=<url>",
            "Add file to prefetch queue with hash verification. Requires begin/end prefetch block.",
            "add prefetch item name:\"app.msi\" sha1:abc123 size:1024 url:\"http://server/app.msi\"",
            "All", "8.0.584.0",
            "At least sha1 OR sha256 required. Supports multiple downloads separated by semicolons."),

        new("add nohash prefetch item", "Download",
            "add nohash prefetch item [name=<name>] [size=<size>] url=<url>",
            "Add file to prefetch queue WITHOUT hash verification (insecure).",
            "add nohash prefetch item name:\"app.msi\" size:1024 url:\"http://server/app.msi\"",
            "All", "8.0.584.0",
            "Insecure - no hash verification. Only one download per item. Relevance substitution NOT allowed."),

        new("end prefetch block", "Download",
            "end prefetch block",
            "End prefetch block. Automatically performs collect prefetch items.",
            "end prefetch block",
            "All", "8.0.584.0",
            "Must be present whenever begin prefetch block is specified. Automatically collects prefetched items."),

        new("collect prefetch items", "Download",
            "collect prefetch items",
            "Download files in prefetch queue and wait. Acts as synchronization point.",
            "collect prefetch items",
            "All", "8.0.584.0",
            "Each call causes action reprocessing from beginning. end prefetch block does automatic collection."),

        new("execute prefetch plug-in", "Download",
            "execute prefetch plug-in <executable-path> [args]",
            "Run external binary in prefetch block. 60-second timeout.",
            "execute prefetch plug-in \"__Download\\plugin.exe\" /downloads \"{parameter \"ini\"}\" \"{download path \"urllist\"}\"",
            "All", "8.0.584.0",
            "Exit code 0 = success. 60 second timeout. If >2 seconds, client logs a message."),

        new("download now", "Download",
            "download now <url>",
            "DEPRECATED. Downloads directly without relay hierarchy.",
            "download now http://download.bigfix.com/update/bfxxxx.exe",
            "All", "8.0.584.0",
            "DEPRECATED - use download or download as instead."),

        // ═══════════════════════════════════════════════════════
        // EXECUTION COMMANDS
        // ═══════════════════════════════════════════════════════
        new("wait", "Execution",
            "wait <command-line>",
            "Run command and wait for completion. Checks exit code.",
            "wait msiexec /i app.msi /q",
            "All", "8.0.584.0",
            "Checks exit code. On Unix, use /bin/sh -c \"command\" for shell commands. Has timeout_seconds and disposition overrides."),

        new("waithidden", "Execution",
            "waithidden <command-line>",
            "Run command hidden (SW_HIDE), wait for completion.",
            "waithidden msiexec /i app.msi /q",
            "Windows", "8.0.584.0",
            "Windows-only. Uses CreateProcess with SW_HIDE. If process requires user input, waits with hidden window."),

        new("waitdetached", "Execution",
            "waitdetached <command-line>",
            "Run command detached (no console), wait.",
            "waitdetached setup.exe /silent",
            "Windows", "8.0.584.0",
            "Windows-only. Sets DETACHED_PROCESS flag. Should not be used for interactive programs."),

        new("run", "Execution",
            "run <command-line>",
            "Run command without waiting.",
            "run notepad.exe",
            "All", "8.0.584.0",
            "Does not wait for process to complete. Fails if process cannot be created. Do not launch long-run programs from __Download."),

        new("runhidden", "Execution",
            "runhidden <command-line>",
            "Run command hidden (SW_HIDE), don't wait.",
            "runhidden setup.exe",
            "Windows", "8.0.584.0",
            "Windows-only. Uses STARTUPINFO dwFlags=STARTF_USESHOWWINDOW, wShowWindow=SW_HIDE."),

        new("rundetached", "Execution",
            "rundetached <command-line>",
            "Run command detached (no console), don't wait.",
            "rundetached setup.exe",
            "Windows", "8.0.584.0",
            "Windows-only. Sets DETACHED_PROCESS flag. Should not be used for interactive programs."),

        new("dos", "Execution",
            "dos <command-line>",
            "Run Windows command using system() API. Fails action on failure.",
            "dos rmdir /Q /S \"C:\\temp\"",
            "Windows", "8.0.584.0",
            "Windows-only. Uses PATH to locate command. Same as system() API call."),

        new("script", "Execution",
            "script <script-name>",
            "Execute JS/VB script using wscript.exe. Blocks until completion.",
            "script attrib.vbs",
            "Windows", "8.0.584.0",
            "Windows-only. Same as wscript + wait. To pass parameters, use run instead."),

        new("script64", "Execution",
            "script64 <script-name>",
            "Execute script in 64-bit context (disables WoW64 redirection first).",
            "script64 attrib.vbs",
            "Windows", "8.0.584.0",
            "Windows-only. Calls Wow64DisableWow64FsRedirection before executing."),

        new("override", "Execution",
            "override <cmd>\nkeyword=value\n<cmd> <rest>",
            "Run command with custom options (timeout, priority, hidden, RunAs).",
            "override wait\nhidden=true\nRunAs=currentuser\nwait app.exe",
            "All", "8.2.531.0",
            "Keywords: Completion, Priority, Hidden, Detached, RunAs, timeout_seconds, disposition."),

        new("action launch preference low-priority", "Execution",
            "action launch preference low-priority",
            "Subsequent commands run at lower priority.",
            "action launch preference low-priority",
            "Windows", "8.0.584.0",
            "Windows-only. Will cause action script to terminate on Unix agent."),

        new("action launch preference normal-priority", "Execution",
            "action launch preference normal-priority",
            "Restore normal priority after low-priority.",
            "action launch preference normal-priority",
            "Windows", "8.0.584.0",
            "Windows-only. Will cause action script to terminate on Unix agent."),

        new("action uses wow64 redirection", "Execution",
            "action uses wow64 redirection <true|false>",
            "Control WoW64 file system redirection. false = 64-bit context.",
            "action uses wow64 redirection false",
            "Windows", "8.0.584.0",
            "Windows-only. Affects dos, delete, copy, move, open, run, wait variants."),

        new("action uses file encoding", "Execution",
            "action uses file encoding <encoding> [NoBOM]",
            "Set encoding for appendfile/createfile. e.g. UTF-8, ISO-8859-1, Shift_JIS.",
            "action uses file encoding UTF-8 NoBOM",
            "All", "9.5.7",
            "Use 'local' to revert to local encoding. UTF encodings have BOM unless NoBOM specified."),

        // ═══════════════════════════════════════════════════════
        // FLOW CONTROL COMMANDS
        // ═══════════════════════════════════════════════════════
        new("if/elseif/else/endif", "Flow Control",
            "if {expr}\n  ...\nelseif {expr}\n  ...\nelse\n  ...\nendif",
            "Conditional execution.",
            "if {name of operating system = \"WinME\"}\n  prefetch patch1.exe ...\nelseif {name of operating system = \"WinXP\"}\n  prefetch patch2.exe ...\nelse\n  prefetch patch3.exe ...\nendif",
            "All", "8.0.584.0",
            "Client pre-parses actions for prefetch. Use cross-platform inspectors to avoid errors."),

        new("continue if", "Flow Control",
            "continue if {condition}",
            "Stop action script if condition is False.",
            "continue if {name of operating system = \"Win2k\"}",
            "All", "8.0.584.0",
            "Stops running action script if relevance evaluates to False."),

        new("pause while", "Flow Control",
            "pause while {condition}",
            "Pause action while condition is True. Continues when False or fails to evaluate.",
            "pause while {exists running application \"updater.exe\"}",
            "All", "8.0.584.0",
            "Continues when expression evaluates to False or fails to evaluate."),

        new("exit", "Flow Control",
            "exit <code>",
            "Terminate action with exit code. Relevance substitution allowed.",
            "exit 0",
            "All", "8.0.584.0",
            "Can also be set by wait, waithidden, waitdetached. For actions of type sh, exit code collected into inspector value."),

        new("parameter", "Flow Control",
            "parameter \"<name>\"=\"<value>\"",
            "Create named variable. Single value only (cannot reassign).",
            "parameter \"loc\"=\"{pathname of folder (value of variable \\\"tmp\\\" of environment)}\"",
            "All", "8.0.584.0",
            "Always stored as string. Fails if relevance produces multiple values. Accessed via parameter inspector."),

        new("action parameter query", "Flow Control",
            "action parameter query \"<name>\" with description \"<desc>\" and with default \"<value>\"",
            "Prompt action creator for parameter value.",
            "action parameter query \"TargetFolder\" with description \"Where to install?\" and with default \"C:\\App\"",
            "All", "8.0.584.0",
            "Relevance substitution NOT allowed on the command line itself. Case sensitive."),

        new("action requires restart", "Flow Control",
            "action requires restart [name]",
            "Force action to Pending Restart state until reboot.",
            "action requires restart",
            "All", "8.0.584.0",
            "Optional name specifies what requires restart (checkable via pending restart inspector)."),

        new("action may require restart", "Flow Control",
            "action may require restart [name]",
            "Check for restart signs. Sets Pending Restart if needed.",
            "action may require restart \"1d9a306f...\"",
            "All", "8.0.584.0",
            "Looks at system for signs that restart is needed. If so, sets action completion to Pending Restart."),

        new("action requires login", "Flow Control",
            "action requires login",
            "Set action to Pending Login until admin logs in after restart.",
            "action requires login",
            "All", "8.0.584.0",
            "Ignored by Unix clients. Makes pending login inspector return true."),

        // ═══════════════════════════════════════════════════════
        // FILE COMMANDS
        // ═══════════════════════════════════════════════════════
        new("delete", "File",
            "delete <filename>",
            "Delete file. Succeeds silently if not found. Fails if exists but cannot delete.",
            "delete \"C:\\temp\\old.exe\"",
            "All", "8.0.584.0",
            "Succeeds if file doesn't exist. Fails if file exists but cannot be deleted (write protection, CD-ROM)."),

        new("copy", "File",
            "copy <source> <destination>",
            "Copy file. Fails if destination already exists or copy fails.",
            "copy \"C:\\source\\file.txt\" \"C:\\dest\\file.txt\"",
            "All", "8.0.584.0",
            "Fails if destination exists. Use delete first. Supports binary name copy for UTF-8 paths."),

        new("move", "File",
            "move <source> <destination>",
            "Move/rename file. Fails if destination exists or source doesn't exist.",
            "move \"C:\\old.exe\" \"C:\\new.exe\"",
            "All", "8.0.584.0",
            "On Windows, use copy+delete for proper permission inheritance. Supports binary name move for UTF-8."),

        new("folder create", "File",
            "folder create <path>",
            "Create directory. Fails if cannot create.",
            "folder create \"C:\\Program Files\\MyApp\"",
            "All", "8.0.584.0",
            "Supports binary name with hex encoding for Unicode folder names."),

        new("folder delete", "File",
            "folder delete <path>",
            "Recursively delete directory and contents. Succeeds if not found.",
            "folder delete \"C:\\temp\"",
            "All", "8.0.584.0",
            "Deletes directory recursively. Succeeds if directory doesn't exist."),

        new("appendfile", "File",
            "appendfile <text>",
            "Append text to __appendfile in site directory.",
            "appendfile Operating System={name of operating system}\nappendfile Windows is installed on the {location of windows folder} drive\nmove __appendfile C:\\info.txt",
            "All", "8.0.584.0",
            "Auto-deleted before action starts. Each invocation appends text. Good for building configs."),

        new("createfile until", "File",
            "createfile until <delimiter>\n...\n<delimiter>",
            "Create file with delimiter-terminated content (here document).",
            "createfile until end\nOperating system = {name of operating system}\nend\ndelete C:\\info.txt\ncopy __createfile C:\\info.txt",
            "All", "8.0.584.0",
            "Creates __createfile in site directory. Auto-deleted before action starts."),

        new("extract", "File",
            "extract <archive-file>",
            "Extract BigFix archive (BFArchive format) to __Download folder.",
            "extract InstallMyApp",
            "All", "8.0.584.0",
            "Uses BFArchive tool. Extracts to __Download folder."),

        new("utility", "File",
            "utility <pathname>",
            "Cache file by SHA-1 to avoid re-downloading.",
            "utility __Download/RunQuiet.exe",
            "All", "8.0.584.0",
            "Maintains two disk caches. Uses sha1 for matching. Least-recently used eviction."),

        new("archive now", "File",
            "archive now",
            "Trigger archive manager. Requires _BESClient_ArchiveManager_OperatingMode = 2.",
            "archive now",
            "All", "8.0.584.0",
            "Fails if operating mode not manual or archive already uploading."),

        // ═══════════════════════════════════════════════════════
        // REGISTRY COMMANDS
        // ═══════════════════════════════════════════════════════
        new("regset", "Registry",
            "regset \"[HKLM\\...\\key]\" \"name\"=\"value\"",
            "Set registry value. Creates key if needed. Uses regedit.exe.",
            "regset \"[HKLM\\SOFTWARE\\MyApp]\" \"Installed\"=\"1\"",
            "Windows", "8.0.584.0",
            "Windows-only. Backslashes in value need double slash \\\\. Use escape of inspector for backslash handling. DWORD: dword:00000002"),

        new("regset64", "Registry",
            "regset64 \"[HKLM\\...\\key]\" \"name\"=\"value\"",
            "Set 64-bit registry value. Disables WoW64 redirection.",
            "regset64 \"[HKLM\\SOFTWARE\\MyApp]\" \"Installed\"=\"1\"",
            "Windows", "8.0.584.0",
            "Windows-only. Calls Wow64DisableWow64FsRedirection before launching 64-bit regedit."),

        new("regdelete", "Registry",
            "regdelete <key> <value-name>",
            "Delete registry value. Creates key if doesn't exist.",
            "regdelete \"[HKEY_CLASSES_ROOT\\ShellScrap]\" \"NeverShowExt\"",
            "Windows", "8.0.584.0",
            "Windows-only. To delete non-empty keys with subkeys, use regedit /s with a .reg file."),

        new("regdelete64", "Registry",
            "regdelete64 <key> <value-name>",
            "Delete 64-bit registry value. Disables WoW64 redirection.",
            "regdelete64 \"[HKEY_CLASSES_ROOT\\ShellScrap]\" \"NeverShowExt\"",
            "Windows", "8.0.584.0",
            "Windows-only. If value doesn't exist, this command will fail."),

        new("regkeydelete", "Registry",
            "regkeydelete <key>",
            "Delete entire key with ALL subkeys and values.",
            "regkeydelete \"[HKEY_LOCAL_MACHINE\\SOFTWARE\\MyKey]\"",
            "Windows", "9.5.13.130",
            "Cannot delete root keys (HKEY_LOCAL_MACHINE etc). Both client and console should be 9.5.13+."),

        new("regkeydelete64", "Registry",
            "regkeydelete64 <key>",
            "Delete entire 64-bit key. Disables WoW64 redirection.",
            "regkeydelete64 \"[HKEY_LOCAL_MACHINE\\SOFTWARE\\MyKey]\"",
            "Windows", "9.5.13.130",
            "Cannot delete root keys. If key doesn't exist, this command fails."),

        // ═══════════════════════════════════════════════════════
        // CLIENT COMMANDS
        // ═══════════════════════════════════════════════════════
        new("restart", "Client",
            "restart [delay-seconds]",
            "Restart computer. Optional delay. Forced (won't prompt to save).",
            "restart 180",
            "All", "8.0.584.0",
            "Delayed restart is forced - won't prompt user to save changes."),

        new("shutdown", "Client",
            "shutdown [delay-seconds]",
            "Shut down computer (no reboot). Optional delay. Forced.",
            "shutdown 60",
            "All", "8.0.584.0",
            "Delayed shutdown is forced - won't prompt user to save changes."),

        new("client restart", "Client",
            "client restart",
            "Restart BES Client service. MUST be LAST command in action script.",
            "client restart",
            "All", "9.0",
            "MUST be LAST command in action script or the command will fail."),

        new("client certificate refresh", "Client",
            "client certificate refresh",
            "Force client certificate update.",
            "client certificate refresh",
            "All", "10.0.7",
            "Issues a request to perform a refresh of its client certificate."),

        new("notify client", "Client",
            "notify client ForceRefresh",
            "Force client refresh. Equivalent to Send Refresh from console.",
            "notify client ForceRefresh",
            "All", "8.0.584.0",
            "Necessary if client can't receive UDP messages."),

        new("setting", "Client",
            "setting \"<name>\"=\"<value>\" on \"<date>\" for client|current site|site \"<sitename>\"",
            "Set named value with timestamp. Latest setting trumps earlier ones.",
            "setting \"MySetting\"=\"true\" on \"{parameter \\\"action issue date\\\" of action}\" for client",
            "All", "8.0.584.0",
            "Recommended date: {parameter \"action issue date\" of action}. Using {now} may override newer settings."),

        new("setting delete", "Client",
            "setting delete \"<name>\" on \"<date>\" for client|current site|site \"<site_url>\"",
            "Delete named setting. Only deletes if delete date > setting date.",
            "setting delete \"MySetting\" on \"{now}\" for client",
            "All", "8.0.584.0",
            "Only deletes if delete date is later than setting date."),

        new("set clock", "Client",
            "set clock",
            "Sync clock with BigFix server. Not available under evaluation license.",
            "set clock",
            "All", "8.0.584.0",
            "Not available when client is operating under an evaluation license."),

        new("relay select", "Client",
            "relay select",
            "Request relay selection at next opportunity.",
            "relay select",
            "All", "8.0.584.0",
            "Always succeeds immediately."),

        new("action lock until", "Client",
            "action lock until \"<expire-date>\" \"<effective-date>\"",
            "Lock client until specified date. Both dates must be time objects.",
            "action lock until \"{now + 3*day}\" \"{now}\"",
            "All", "8.0.584.0",
            "Both dates must be time objects."),

        new("action lock indefinite", "Client",
            "action lock indefinite \"<effective-date>\"",
            "Lock client indefinitely.",
            "action lock indefinite \"{now}\"",
            "All", "8.0.584.0",
            "Locks the client starting on the effective date."),

        new("action unlock", "Client",
            "action unlock \"<effective-date>\"",
            "Unlock client. Effective date ensures ordering.",
            "action unlock \"{now}\"",
            "All", "8.0.584.0",
            "Effective date ensures locking/unlocking actions take place in order."),

        new("action log command", "Client",
            "action log command",
            "Log commands only (not parameters). For sensitive info.",
            "action log command",
            "All", "8.2.474.0",
            "Use to avoid logging sensitive info like private keys or passwords."),

        new("action log all", "Client",
            "action log all",
            "Log all commands and parameters (default behavior).",
            "action log all",
            "All", "8.2.474.0",
            "Default behavior. Used to undo action log command."),

        new("administrator add", "Client",
            "administrator add <operator-name> on <date>",
            "Add BigFix user as administrator.",
            "administrator add \"bob\" on \"21 Aug 2002 17:39:14 gmt\"",
            "All", "8.0.584.0",
            "operator-name is the masthead user name. Use masthead operator name session inspector."),

        new("administrator delete", "Client",
            "administrator delete <operator-name> on <date>",
            "Remove BigFix user as administrator.",
            "administrator delete \"bob\" on \"21 Aug 2002 17:39:14 gmt\"",
            "All", "8.0.584.0",
            "operator-name is the masthead user name."),

        new("plugin store", "Client",
            "plugin store \"<pluginName>\" set|delete ...",
            "Insert/update/delete BigFix cloud plugin settings.",
            "plugin store \"MyPlugin\" set \"UName\" value \"JUser\" on \"31 Jan 2007 21:09:36 gmt\"",
            "Windows, Red Hat", "10.0.0.0",
            "Supports encrypted values. Multiple set via JSON."),

        // ═══════════════════════════════════════════════════════
        // SITE COMMANDS
        // ═══════════════════════════════════════════════════════
        new("site force evaluation", "Site",
            "site force evaluation",
            "Re-evaluate all content in site.",
            "site force evaluation",
            "All", "8.0.584.0",
            "Can place more load on client machine; should probably not be used."),

        new("subscribe", "Site",
            "subscribe <masthead-file>",
            "Subscribe to site via masthead file.",
            "subscribe \"__Download\\site.fxm\"",
            "All", "8.0.584.0",
            "Should use Console instead. Only works from master action site."),

        new("unsubscribe", "Site",
            "unsubscribe",
            "Unsubscribe from current site.",
            "unsubscribe",
            "All", "8.0.584.0",
            "Should use Console instead."),

        // ═══════════════════════════════════════════════════════
        // AGENT TO AGENT COMMANDS
        // ═══════════════════════════════════════════════════════
        new("agent interface", "Agent2Agent",
            "agent interface \"ProductID\" command",
            "Pass instructions to specific agent via A2A channel.",
            "agent interface \"My_Prod\" quarantine file -filepath \"C:\\myfolder\\myfile.exe\"",
            "All", "9.5.5.0",
            "Requires Install BigFix A2A Fixlet DLLs. A2A channel implemented by DLL files."),
    };

    public static List<CommandInfo> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return All;
        return All.Where(c =>
            c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            c.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            c.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            c.Syntax.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static List<CommandInfo> ByCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category) || category == "All") return All;
        return All.Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static List<string> Categories => new() { "All", "Download", "Execution", "Flow Control", "File", "Registry", "Client", "Site", "Agent2Agent" };

    public static class Snippets
    {
        public static string KillProcess(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return "";
            return $"waithidden powershell -Command \"Stop-Process -Name '{processName}' -Force -ErrorAction SilentlyContinue\"";
        }

        public static string StopService(string serviceName)
        {
            if (string.IsNullOrWhiteSpace(serviceName)) return "";
            return $"waithidden sc stop \"{serviceName}\"";
        }

        public static string DeleteFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return "";
            return $"delete \"{filePath}\"";
        }

        public static string DeleteFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return "";
            return $"folder delete \"{folderPath}\"";
        }

        public static string CopyFile(string source, string dest)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(dest)) return "";
            return $"copy \"{source}\" \"{dest}\"";
        }

        public static string MoveFile(string source, string dest)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(dest)) return "";
            return $"move \"{source}\" \"{dest}\"";
        }

        public static string CreateFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            return $"folder create \"{path}\"";
        }

        public static string SetRegistry(string key, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name)) return "";
            return $"regset \"[{key}]\" \"{name}\"=\"{value}\"";
        }

        public static string SetRegistry64(string key, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name)) return "";
            return $"regset64 \"[{key}]\" \"{name}\"=\"{value}\"";
        }

        public static string DeleteRegistry(string key, string name)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name)) return "";
            return $"regdelete \"[{key}]\" \"{name}\"";
        }

        public static string DeleteRegistryKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "";
            return $"regkeydelete \"[{key}]\"";
        }

        public static string RunCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return "";
            return $"wait {command}";
        }

        public static string RunHidden(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return "";
            return $"waithidden {command}";
        }

        public static string RestartComputer(int delaySeconds = 0)
        {
            return delaySeconds > 0 ? $"restart {delaySeconds}" : "restart";
        }

        public static string ShutdownComputer(int delaySeconds = 0)
        {
            return delaySeconds > 0 ? $"shutdown {delaySeconds}" : "shutdown";
        }

        public static string ContinueIfRelevant(string condition)
        {
            if (string.IsNullOrWhiteSpace(condition)) return "";
            return $"continue if {{{condition}}}";
        }

        public static string PauseWhile(string condition)
        {
            if (string.IsNullOrWhiteSpace(condition)) return "";
            return $"pause while {{{condition}}}";
        }

        public static string SetParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return $"parameter \"{name}\"=\"{value}\"";
        }

        public static string CreateConfigFile(string fileName, string content)
        {
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(content)) return "";
            return $"createfile until end_of_file\n{content}\nend_of_file\nmove __createfile \"{fileName}\"";
        }

        public static string AppendToFile(string fileName, string line)
        {
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(line)) return "";
            return $"appendfile {line}\nmove __appendfile \"{fileName}\"";
        }

        public static string ActionRequiresRestart()
        {
            return "action requires restart";
        }

        public static string ActionRequiresLogin()
        {
            return "action requires login";
        }

        public static string ActionMayRequireRestart()
        {
            return "action may require restart";
        }

        public static string ClientRestart()
        {
            return "client restart";
        }

        public static string ForceRefresh()
        {
            return "notify client ForceRefresh";
        }

        public static string SetClientSetting(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return $"setting \"{name}\"=\"{value}\" on \"{{parameter \\\"action issue date\\\" of action}}\" for client";
        }

        public static string DeleteClientSetting(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return $"setting delete \"{name}\" on \"{{now}}\" for client";
        }

        public static string ActionLockUntil(int days)
        {
            return $"action lock until \"{{now + {days}*day}}\" \"{{now}}\"";
        }

        public static string ActionLockIndefinite()
        {
            return "action lock indefinite \"{now}\"";
        }

        public static string ActionUnlock()
        {
            return "action unlock \"{now}\"";
        }
    }
}
