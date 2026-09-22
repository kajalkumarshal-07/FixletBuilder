namespace FixletBuilder.Core;

public static class SilentArgsDatabase
{
    private static readonly Dictionary<string, (string Install, string Uninstall)> KnownArgs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Google.Chrome"] = ("/silent /install", "--uninstall --force"),
        ["Mozilla.Firefox"] = ("-ms /quiet", "--silent --uninstall"),
        ["Microsoft.VisualStudioCode"] = ("/silent /mergetasks=!runcode", "/uninstall /quiet"),
        ["Notepad++.Notepad++"] = ("-q", "--uninstall -q"),
        ["7-Zip.7-Zip"] = ("/S", "/S"),
        ["VideoLAN.VLC"] = ("/S /L=1033", "/S"),
        ["Adobe.Acrobat.Reader"] = ("/sAll /msEulaAccepted /qn", ""),
        ["Oracle.JavaRuntimeEnvironment"] = ("/s", ""),
        ["Microsoft.DotNet.Runtime"] = ("/install /quiet /norestart", ""),
        ["Microsoft.WindowsTerminal"] = ("/silent", ""),
        ["Git.Git"] = ("/VERYSILENT /NORESTART", ""),
        ["PuTTY.PuTTY"] = ("/silent", ""),
        ["Zoom.Zoom"] = ("/silent", ""),
        ["SlackTechnologies.Slack"] = ("/silent", ""),
        ["Discord.Discord"] = ("/silent", ""),
        ["Spotify.Spotify"] = ("/silent", ""),
        ["Microsoft.OneDrive"] = ("/silent", ""),
        ["Microsoft.Teams"] = ("/silent /update", ""),
        ["Amazon.AWSCLI"] = ("/quiet", ""),
        ["Docker.DockerDesktop"] = ("/quiet", ""),
        ["NodeJS.NodeJS"] = ("/quiet", ""),
        ["Python.Python.3"] = ("/quiet InstallAllUsers=1 PrependPath=1", ""),
        ["RubyInstaller.Ruby"] = ("/silent", ""),
        ["GoLang.Go"] = ("/silent", ""),
        ["Rustlang.Rust.MSVC"] = ("/quiet", ""),
        ["Microsoft.SQLServerManagementStudio"] = ("/install /quiet /norestart", ""),
        ["Microsoft.SQLServer2022"] = ("/quiet IACCEPTSQLSERVERLICENSETERMS=1", ""),
        ["Mozilla.Thunderbird"] = ("-ms /quiet", ""),
        ["CrystalDewWorld.CrystalDiskInfo"] = ("/S", ""),
        ["Inkscape.Inkscape"] = ("/S", ""),
        ["GIMP.GIMP"] = ("/S", ""),
        ["BlenderFoundation.Blender"] = ("/S", ""),
        ["KeePassXCTest.KeePassXC"] = ("/S", ""),
        ["1Password.1Password"] = ("/silent", ""),
        ["Bitwarden.Bitwarden"] = ("/silent", ""),
        ["Mozilla.Firefox.ESR"] = ("-ms /quiet", ""),
        ["Apple.iTunes"] = ("/quiet", ""),
        ["Microsoft.SQLServerReportingServices"] = ("/quiet /norestart", ""),
        ["Microsoft.ExchangeServer"] = ("/quiet /norestart", ""),
        ["VMware.WorkstationPro"] = ("/quiet /s /v\"/qn\"", ""),
        ["Oracle.VirtualBox"] = ("-q -s", ""),
        ["HashiCorp.Vagrant"] = ("/quiet", ""),
        ["VisualStudioCode.Insiders"] = ("/silent", ""),
    };

    private static readonly Dictionary<string, string> PackageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Google.Chrome",
        ["firefox"] = "Mozilla.Firefox",
        ["vscode"] = "Microsoft.VisualStudioCode",
        ["notepad++"] = "Notepad++.Notepad++",
        ["notepadplus"] = "Notepad++.Notepad++",
        ["7zip"] = "7-Zip.7-Zip",
        ["vlc"] = "VideoLAN.VLC",
        ["acrobat"] = "Adobe.Acrobat.Reader",
        ["java"] = "Oracle.JavaRuntimeEnvironment",
        ["dotnet"] = "Microsoft.DotNet.Runtime",
        ["terminal"] = "Microsoft.WindowsTerminal",
        ["putty"] = "PuTTY.PuTTY",
        ["zoom"] = "Zoom.Zoom",
        ["slack"] = "SlackTechnologies.Slack",
        ["discord"] = "Discord.Discord",
        ["spotify"] = "Spotify.Spotify",
        ["onedrive"] = "Microsoft.OneDrive",
        ["teams"] = "Microsoft.Teams",
        ["aws"] = "Amazon.AWSCLI",
        ["docker"] = "Docker.DockerDesktop",
        ["node"] = "NodeJS.NodeJS",
        ["nodejs"] = "NodeJS.NodeJS",
        ["python"] = "Python.Python.3",
        ["ruby"] = "RubyInstaller.Ruby",
        ["go"] = "GoLang.Go",
        ["golang"] = "GoLang.Go",
        ["rust"] = "Rustlang.Rust.MSVC",
        ["ssms"] = "Microsoft.SQLServerManagementStudio",
        ["thunderbird"] = "Mozilla.Thunderbird",
        ["gimp"] = "GIMP.GIMP",
        ["inkscape"] = "Inkscape.Inkscape",
        ["blender"] = "BlenderFoundation.Blender",
        ["keepassxc"] = "KeePassXCTest.KeePassXC",
        ["1password"] = "1Password.1Password",
        ["bitwarden"] = "Bitwarden.Bitwarden",
        ["itunes"] = "Apple.iTunes",
        ["vbox"] = "Oracle.VirtualBox",
        ["virtualbox"] = "Oracle.VirtualBox",
        ["vmware"] = "VMware.WorkstationPro",
        ["vagrant"] = "HashiCorp.Vagrant"
    };

    public static string GetForPackage(string packageId)
    {
        var resolved = ResolvePackageId(packageId);

        if (KnownArgs.TryGetValue(resolved, out var args))
            return args.Install;

        var normalized = resolved.Replace(".", "");
        foreach (var kv in KnownArgs)
        {
            if (kv.Key.Replace(".", "").Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return kv.Value.Install;
        }

        return "/silent";
    }

    public static string GetUninstallForPackage(string packageId)
    {
        if (KnownArgs.TryGetValue(packageId, out var args))
            return args.Uninstall;

        return "";
    }

    public static string ResolvePackageId(string input)
    {
        if (PackageAliases.TryGetValue(input, out var id))
            return id;

        return input;
    }

    public static string GetForApp(string appName)
    {
        var normalized = appName.Replace(" ", "").Replace(".", "");
        foreach (var kv in PackageAliases)
        {
            if (kv.Key.Replace(" ", "").Replace(".", "").Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return KnownArgs.TryGetValue(kv.Value, out var args) ? args.Install : "/silent";
            }
        }

        foreach (var kv in KnownArgs)
        {
            if (kv.Key.Replace(".", "").Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(kv.Key.Replace(".", ""), StringComparison.OrdinalIgnoreCase))
            {
                return kv.Value.Install;
            }
        }

        return "/silent";
    }
}
