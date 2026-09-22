using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FixletBuilder.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FixletBuilder;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TypeCombo.ItemsSource = FixletTemplates.Types;
        TypeCombo.SelectedIndex = 0;

        RefCategoryCombo.ItemsSource = ActionScriptCommands.Categories;
        RefCategoryCombo.SelectedIndex = 0;

        var s = AppSettings.Current;
        if (s.WindowLeft is double l && s.WindowTop is double t &&
            s.WindowWidth is double w && s.WindowHeight is double h &&
            w >= MinWidth && h >= MinHeight)
        {
            Left = l;
            Top = t;
            Width = w;
            Height = h;
        }

        foreach (var b in new[]
                 {
                     AppNameBox, VersionBox, SourceUrlBox, SilentArgsBox, InstallPathBox, UninstallStringBox,
                     RegistryKeyBox, RegistryValueNameBox, RegistryValueBox,
                     ProcessNameBox, ServiceNameBox,
                     TitleBox, CategoryBox, SourceBox, SourceIdBox, DomainBox, DownloadSizeBox,
                     Sha1Box, Sha256Box, FileSizeBox,
                     RelevanceBox, DescriptionBox, ActionScriptBox, SuccessBox
                 })
            b.TextChanged += (_, _) => UpdatePreview();

        UpdatePreview();

        var services = CliRunner.BuildServices();
        var winget = services.GetRequiredService<IWingetIntegration>();
        WingetStatus.Text = winget.IsAvailable() ? "winget: available" : "winget: not found";
    }

    private void ApplyTemplateBtn_Click(object sender, RoutedEventArgs e)
    {
        var type = TypeCombo.SelectedItem as string ?? FixletTemplates.TypeInstall;
        var sha1 = Sha1Box.Text.Trim();
        var sha256 = Sha256Box.Text.Trim();
        long.TryParse(FileSizeBox.Text.Trim(), out long fileSizeBytes);
        var registryKeyPath = RegistryKeyBox.Text.Trim();
        var registryValueName = RegistryValueNameBox.Text.Trim();
        var registryValue = RegistryValueBox.Text.Trim();
        var processToKill = ProcessNameBox.Text.Trim();
        var serviceToStop = ServiceNameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(registryValueName))
            registryValueName = "DisplayVersion";

        var t = FixletTemplates.Apply(
            type,
            AppNameBox.Text.Trim(),
            VersionBox.Text.Trim(),
            SourceUrlBox.Text.Trim(),
            SilentArgsBox.Text.Trim(),
            InstallPathBox.Text.Trim(),
            UninstallStringBox.Text.Trim(),
            sha1: sha1,
            sha256: sha256,
            fileSizeBytes: fileSizeBytes,
            processToKill: processToKill,
            serviceToStop: serviceToStop);

        var relevance = RelevanceBuilder.BuildForInstall(
            InstallPathBox.Text.Trim(),
            AppNameBox.Text.Trim(),
            VersionBox.Text.Trim(),
            registryKeyPath, registryValueName, registryValue);

        if (type == "upgrade")
            relevance = RelevanceBuilder.BuildForUpgrade(
                InstallPathBox.Text.Trim(),
                AppNameBox.Text.Trim(),
                VersionBox.Text.Trim(),
                VersionBox.Text.Trim(),
                registryKeyPath, registryValueName, registryValue);
        else if (type == "uninstall")
            relevance = RelevanceBuilder.BuildForUninstall(
                InstallPathBox.Text.Trim(),
                AppNameBox.Text.Trim(),
                UninstallStringBox.Text.Trim(),
                registryKeyPath, registryValueName, registryValue);

        var successCriteria = RelevanceBuilder.BuildSuccessCriteria(
            InstallPathBox.Text.Trim(),
            type,
            VersionBox.Text.Trim(),
            registryKeyPath, registryValueName);

        TitleBox.Text = t.Title;
        RelevanceBox.Text = string.Join(Environment.NewLine, relevance);
        DescriptionBox.Text = t.Description;
        ActionScriptBox.Text = t.ActionScript;
        SuccessBox.Text = successCriteria;

        UpdatePreview();
        StatusText.Text = "Template applied. Review the relevance and script, then edit as needed.";
    }

    private void NewFixletBtn_Click(object sender, RoutedEventArgs e)
    {
        TypeCombo.SelectedIndex = 0;
        foreach (var b in new[]
                 {
                     AppNameBox, VersionBox, SourceUrlBox, SilentArgsBox, InstallPathBox, UninstallStringBox,
                     RegistryKeyBox, RegistryValueBox,
                     ProcessNameBox, ServiceNameBox,
                     TitleBox, SourceBox, SourceIdBox, DomainBox, DownloadSizeBox,
                     Sha1Box, Sha256Box, FileSizeBox,
                     RelevanceBox, DescriptionBox, ActionScriptBox, SuccessBox
                 })
            b.Clear();
        CategoryBox.Text = "Applications";
        RegistryValueNameBox.Text = "DisplayVersion";

        UpdatePreview();
        StatusText.Text = "New fixlet started.";
    }

    private FixletModel BuildModel()
    {
        var type = TypeCombo.SelectedItem as string ?? FixletTemplates.TypeInstall;
        var sha1 = Sha1Box.Text.Trim();
        var sha256 = Sha256Box.Text.Trim();
        long.TryParse(FileSizeBox.Text.Trim(), out long fileSizeBytes);
        var registryKeyPath = RegistryKeyBox.Text.Trim();
        var registryValueName = RegistryValueNameBox.Text.Trim();
        var registryValue = RegistryValueBox.Text.Trim();
        var processToKill = ProcessNameBox.Text.Trim();
        var serviceToStop = ServiceNameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(registryValueName))
            registryValueName = "DisplayVersion";

        var template = FixletTemplates.Apply(
            type,
            AppNameBox.Text.Trim(),
            VersionBox.Text.Trim(),
            SourceUrlBox.Text.Trim(),
            SilentArgsBox.Text.Trim(),
            InstallPathBox.Text.Trim(),
            UninstallStringBox.Text.Trim(),
            sha1: sha1,
            sha256: sha256,
            fileSizeBytes: fileSizeBytes,
            processToKill: processToKill,
            serviceToStop: serviceToStop);

        var relevance = RelevanceBuilder.BuildForInstall(
            InstallPathBox.Text.Trim(),
            AppNameBox.Text.Trim(),
            VersionBox.Text.Trim(),
            registryKeyPath, registryValueName, registryValue);

        if (type == "upgrade")
            relevance = RelevanceBuilder.BuildForUpgrade(
                InstallPathBox.Text.Trim(),
                AppNameBox.Text.Trim(),
                VersionBox.Text.Trim(),
                VersionBox.Text.Trim(),
                registryKeyPath, registryValueName, registryValue);
        else if (type == "uninstall")
            relevance = RelevanceBuilder.BuildForUninstall(
                InstallPathBox.Text.Trim(),
                AppNameBox.Text.Trim(),
                UninstallStringBox.Text.Trim(),
                registryKeyPath, registryValueName, registryValue);

        var successCriteria = RelevanceBuilder.BuildSuccessCriteria(
            InstallPathBox.Text.Trim(),
            type,
            VersionBox.Text.Trim(),
            registryKeyPath, registryValueName);

        var model = new FixletModel
        {
            Title = TitleBox.Text.Trim(),
            Category = CategoryBox.Text.Trim(),
            Source = SourceBox.Text.Trim(),
            SourceId = SourceIdBox.Text.Trim(),
            Domain = DomainBox.Text.Trim(),
            Description = DescriptionBox.Text,
            Relevance = RelevanceBox.Text
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList(),
            ActionDescription = template.ActionDescription,
            ActionScript = ActionScriptBox.Text,
            SuccessCriteria = SuccessBox.Text.Trim(),
            Sha1 = sha1,
            Sha256 = sha256,
            FileSizeBytes = fileSizeBytes > 0 ? fileSizeBytes : null,
            RegistryKeyPath = registryKeyPath,
            RegistryValueName = registryValueName,
            RegistryValue = registryValue
        };

        if (model.Title.Length == 0)
            model.Title = template.Title;
        if (model.Category.Length == 0)
            model.Category = "Applications";
        if (double.TryParse(DownloadSizeBox.Text.Trim(), System.Globalization.CultureInfo.InvariantCulture, out double size) && size > 0)
            model.DownloadSizeMb = size;

        return model;
    }

    private void UpdatePreview()
    {
        var model = BuildModel();
        var writer = new FixletWriter(Microsoft.Extensions.Logging.Abstractions.NullLogger<FixletWriter>.Instance);
        XmlHighlight.Apply(XmlPreviewBox, writer.GenerateXml(model));
        CountsText.Text =
            $"Relevance: {model.Relevance.Count} expression(s)  |  " +
            $"Script: {(model.ActionScript.Length == 0 ? 0 : model.ActionScript.Split('\n').Length)} lines";
    }

    private void ValidateBtn_Click(object sender, RoutedEventArgs e)
    {
        var issues = FixletFactory.Validate(BuildModel());
        if (issues.Count == 0)
        {
            StatusText.Text = "Fixlet is valid.";
            MessageBox.Show(this, "Fixlet is valid.", "Validation", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            StatusText.Text = $"{issues.Count} issue(s) found.";
            MessageBox.Show(this, string.Join(Environment.NewLine, issues), "Validation issues", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var model = BuildModel();
        var issues = FixletFactory.Validate(model);
        if (issues.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, issues), "Validation issues", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = AppSettings.Current;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Fixlet",
            FileName = FixletFactory.SanitizeFileName(model.Title) + ".bes",
            Filter = "BigFix Fixlet (*.bes)|*.bes|All files (*.*)|*.*",
            DefaultExt = ".bes"
        };
        if (!string.IsNullOrWhiteSpace(settings.LastSaveFolder) && Directory.Exists(settings.LastSaveFolder))
            dialog.InitialDirectory = settings.LastSaveFolder;

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var writer = new FixletWriter(Microsoft.Extensions.Logging.Abstractions.NullLogger<FixletWriter>.Instance);
            writer.WriteBes(dialog.FileName, model);
            settings.LastSaveFolder = Path.GetDirectoryName(dialog.FileName) ?? "";
            settings.Save();
            StatusText.Text = $"Saved: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save file:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BatchImportBtn_Click(object sender, RoutedEventArgs e)
    {
        new ImportWindow { Owner = this }.ShowDialog();
        UpdatePreview();
    }

    private void ScanAppsBtn_Click(object sender, RoutedEventArgs e)
    {
        new ScanWindow { Owner = this }.ShowDialog();
        UpdatePreview();
    }

    private async void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
    {
        var services = CliRunner.BuildServices();
        var winget = services.GetRequiredService<IWingetIntegration>();
        if (!winget.IsAvailable())
        {
            MessageBox.Show(this, "winget is not installed or not available.", "Winget", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CheckUpdatesBtn.IsEnabled = false;
        StatusText.Text = "Checking for updates...";

        try
        {
            var monitor = services.GetRequiredService<IAppVersionMonitor>();
            var writer = new FixletWriter(Microsoft.Extensions.Logging.Abstractions.NullLogger<FixletWriter>.Instance);
            var results = await monitor.CheckAllAsync();
            var updates = results.Where(r => r.UpdateAvailable).ToList();

            if (updates.Count == 0)
            {
                StatusText.Text = "All tracked apps are up to date.";
                MessageBox.Show(this, "No updates available for tracked apps.", "Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var message = $"{updates.Count} update(s) available:\n\n";
            foreach (var u in updates.Take(20))
                message += $"  {u.AppName}: {u.InstalledVersion} -> {u.LatestVersion}\n";

            var result = MessageBox.Show(this, message + "\nGenerate upgrade fixlets?", "Updates Available",
                MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                var fixlets = monitor.GenerateUpgradeFixlets(results);
                var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixlet-output");
                Directory.CreateDirectory(outputDir);

                foreach (var fixlet in fixlets)
                {
                    var fileName = FixletFactory.SanitizeFileName(fixlet.Title) + ".bes";
                    writer.WriteBes(Path.Combine(outputDir, fileName), fixlet);
                }

                StatusText.Text = $"{fixlets.Count} upgrade fixlet(s) generated.";
                MessageBox.Show(this, $"{fixlets.Count} upgrade fixlet(s) generated in:\n{outputDir}", "Done",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText.Text = $"{updates.Count} update(s) found.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Update check failed: {ex.Message}";
        }
        finally
        {
            CheckUpdatesBtn.IsEnabled = true;
        }
    }

    private async void AutoFetchBtn_Click(object sender, RoutedEventArgs e)
    {
        var services = CliRunner.BuildServices();
        var winget = services.GetRequiredService<IWingetIntegration>();
        if (!winget.IsAvailable())
        {
            MessageBox.Show(this, "winget is not installed or not available.", "Winget", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var appName = AppNameBox.Text.Trim();
        if (appName.Length == 0)
        {
            MessageBox.Show(this, "Enter an app name first.", "Auto-Fetch", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AutoFetchBtn.IsEnabled = false;
        StatusText.Text = $"Fetching metadata for '{appName}'...";

        try
        {
            var searchResults = await winget.SearchAsync(appName);
            var bestMatch = searchResults.FirstOrDefault();

            if (bestMatch is null)
            {
                StatusText.Text = "No matching package found.";
                MessageBox.Show(this, $"No winget package found for '{appName}'.", "Auto-Fetch", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var details = await winget.GetDetailsAsync(bestMatch.Id);
            if (details is null)
            {
                StatusText.Text = "Could not fetch package details.";
                return;
            }

            if (string.IsNullOrWhiteSpace(VersionBox.Text))
                VersionBox.Text = details.Version;
            if (string.IsNullOrWhiteSpace(SourceUrlBox.Text))
                SourceUrlBox.Text = details.InstallerUrl;
            if (string.IsNullOrWhiteSpace(SilentArgsBox.Text))
                SilentArgsBox.Text = details.SilentArgs;
            if (string.IsNullOrWhiteSpace(InstallPathBox.Text))
                InstallPathBox.Text = details.InstallLocation;

            SourceIdBox.Text = bestMatch.Id;

            // Try to find registry key for this app
            var regKey = FindRegistryKeyForApp(appName);
            if (!string.IsNullOrWhiteSpace(regKey) && string.IsNullOrWhiteSpace(RegistryKeyBox.Text))
                RegistryKeyBox.Text = regKey;

            if (string.IsNullOrWhiteSpace(RegistryValueBox.Text) && !string.IsNullOrWhiteSpace(details.Version))
                RegistryValueBox.Text = details.Version;

            var type = TypeCombo.SelectedItem as string ?? FixletTemplates.TypeInstall;
            var registryKeyPath = RegistryKeyBox.Text.Trim();
            var registryValueName = RegistryValueNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(registryValueName)) registryValueName = "DisplayVersion";
            var registryValue = RegistryValueBox.Text.Trim();

            var relevance = RelevanceBuilder.BuildForInstall(details.InstallLocation, appName, details.Version,
                registryKeyPath, registryValueName, registryValue);
            RelevanceBox.Text = string.Join(Environment.NewLine, relevance);

            if (string.IsNullOrWhiteSpace(SuccessBox.Text))
                SuccessBox.Text = RelevanceBuilder.BuildSuccessCriteria(details.InstallLocation, type, details.Version,
                    registryKeyPath, registryValueName);

            UpdatePreview();
            StatusText.Text = $"Auto-filled from winget package: {bestMatch.Id}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Auto-fetch failed: {ex.Message}";
        }
        finally
        {
            AutoFetchBtn.IsEnabled = true;
        }
    }

    private static string FindRegistryKeyForApp(string appName)
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
        }
        catch
        {
            // ignore
        }
        return "";
    }

    private async void FetchHashesBtn_Click(object sender, RoutedEventArgs e)
    {
        var url = SourceUrlBox.Text.Trim();
        if (url.Length == 0)
        {
            MessageBox.Show(this, "Enter an Installer URL first, then click Fetch Hashes.", "Fetch Hashes", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        FetchHashesBtn.IsEnabled = false;
        StatusText.Text = "Downloading file and computing hashes...";

        try
        {
            var services = CliRunner.BuildServices();
            var downloadService = services.GetRequiredService<IDownloadService>();
            var hashInfo = await downloadService.GetFileHashInfoAsync(url);

            if (hashInfo is null)
            {
                StatusText.Text = "Failed to download or hash the file.";
                MessageBox.Show(this, "Could not download or hash the file from the provided URL.", "Fetch Hashes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Sha1Box.Text = hashInfo.Sha1;
            Sha256Box.Text = hashInfo.Sha256;
            FileSizeBox.Text = hashInfo.FileSizeBytes.ToString();

            UpdatePreview();
            StatusText.Text = $"Hashes computed: SHA1={hashInfo.Sha1[..12]}... SHA256={hashInfo.Sha256[..12]}... Size={hashInfo.FileSizeBytes} bytes";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Hash computation failed: {ex.Message}";
        }
        finally
        {
            FetchHashesBtn.IsEnabled = true;
        }
    }

    private void SnippetKillProcess_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptSnippetValue("Kill Process", "Enter process name to kill:", "notepad");
        if (name != null) InsertIntoScript($"waithidden powershell -Command \"Stop-Process -Name '{name}' -Force -ErrorAction SilentlyContinue\"", SnippetPosition.BeforeInstall);
    }

    private void SnippetStopService_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptSnippetValue("Stop Service", "Enter service name to stop:", "W3SVC");
        if (name != null) InsertIntoScript($"waithidden sc stop \"{name}\"", SnippetPosition.BeforeInstall);
    }

    private void SnippetDeleteFile_Click(object sender, RoutedEventArgs e)
    {
        var path = PromptSnippetValue("Delete File", "Enter file path to delete:", @"C:\temp\file.exe");
        if (path != null) InsertIntoScript($"delete \"{path}\"", SnippetPosition.BeforeInstall);
    }

    private void SnippetDeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = PromptSnippetValue("Delete Folder", "Enter folder path to delete:", @"C:\temp");
        if (path != null) InsertIntoScript($"folder delete \"{path}\"", SnippetPosition.BeforeInstall);
    }

    private void SnippetCopyFile_Click(object sender, RoutedEventArgs e)
    {
        var src = PromptSnippetValue("Copy File", "Enter source file path:", @"C:\source\file.txt");
        if (src == null) return;
        var dst = PromptSnippetValue("Copy File", "Enter destination path:", @"C:\dest\file.txt");
        if (dst == null) return;
        InsertIntoScript($"copy \"{src}\" \"{dst}\"", SnippetPosition.BeforeInstall);
    }

    private void SnippetMoveFile_Click(object sender, RoutedEventArgs e)
    {
        var src = PromptSnippetValue("Move File", "Enter source file path:", @"C:\old.exe");
        if (src == null) return;
        var dst = PromptSnippetValue("Move File", "Enter destination path:", @"C:\new.exe");
        if (dst == null) return;
        InsertIntoScript($"move \"{src}\" \"{dst}\"", SnippetPosition.BeforeInstall);
    }

    private void SnippetCreateFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = PromptSnippetValue("Create Folder", "Enter folder path to create:", @"C:\Program Files\MyApp");
        if (path != null) InsertIntoScript($"folder create \"{path}\"", SnippetPosition.BeforeInstall);
    }

    private void SnippetSetRegistry_Click(object sender, RoutedEventArgs e)
    {
        var key = PromptSnippetValue("Set Registry", "Enter registry key (without brackets):", @"HKLM\SOFTWARE\MyApp");
        if (key == null) return;
        var name = PromptSnippetValue("Set Registry", "Enter value name:", "Installed");
        if (name == null) return;
        var val = PromptSnippetValue("Set Registry", "Enter value data:", "1");
        if (val != null) InsertIntoScript($"regset \"[{key}]\" \"{name}\"=\"{val}\"", SnippetPosition.BeforeInstall);
    }

    private void SnippetSetRegistry64_Click(object sender, RoutedEventArgs e)
    {
        var key = PromptSnippetValue("Set Reg 64", "Enter registry key (without brackets):", @"HKLM\SOFTWARE\MyApp");
        if (key == null) return;
        var name = PromptSnippetValue("Set Reg 64", "Enter value name:", "Installed");
        if (name == null) return;
        var val = PromptSnippetValue("Set Reg 64", "Enter value data:", "1");
        if (val != null) InsertIntoScript($"regset64 \"[{key}]\" \"{name}\"=\"{val}\"", SnippetPosition.BeforeInstall);
    }

    private void SnippetDeleteRegistry_Click(object sender, RoutedEventArgs e)
    {
        var key = PromptSnippetValue("Delete Registry Value", "Enter registry key (without brackets):", @"HKLM\SOFTWARE\MyApp");
        if (key == null) return;
        var name = PromptSnippetValue("Delete Registry Value", "Enter value name to delete:", "OldValue");
        if (name != null) InsertIntoScript($"regdelete \"[{key}]\" \"{name}\"", SnippetPosition.BeforeInstall);
    }

    private void SnippetDeleteRegistryKey_Click(object sender, RoutedEventArgs e)
    {
        var key = PromptSnippetValue("Delete Registry Key", "Enter registry key to delete (without brackets):", @"HKLM\SOFTWARE\MyApp");
        if (key != null) InsertIntoScript($"regkeydelete \"[{key}\"", SnippetPosition.BeforeInstall);
    }

    private void SnippetRestart_Click(object sender, RoutedEventArgs e)
    {
        var delay = PromptSnippetValue("Restart", "Enter delay in seconds (0 for immediate):", "180");
        if (delay != null) InsertIntoScript($"restart {delay}", SnippetPosition.AfterInstall);
    }

    private void SnippetShutdown_Click(object sender, RoutedEventArgs e)
    {
        var delay = PromptSnippetValue("Shutdown", "Enter delay in seconds (0 for immediate):", "60");
        if (delay != null) InsertIntoScript($"shutdown {delay}", SnippetPosition.AfterInstall);
    }

    private void SnippetClientRestart_Click(object sender, RoutedEventArgs e)
    {
        InsertIntoScript("client restart", SnippetPosition.End);
    }

    private void SnippetForceRefresh_Click(object sender, RoutedEventArgs e)
    {
        InsertIntoScript("notify client ForceRefresh", SnippetPosition.End);
    }

    private void SnippetContinueIf_Click(object sender, RoutedEventArgs e)
    {
        var rel = PromptSnippetValue("Continue If", "Enter relevance condition:", "name of operating system = \"Win10\"");
        if (rel != null) InsertIntoScript($"continue if {{{rel}}}", SnippetPosition.Top);
    }

    private void SnippetPauseWhile_Click(object sender, RoutedEventArgs e)
    {
        var rel = PromptSnippetValue("Pause While", "Enter relevance condition:", "exists running application \"updater.exe\"");
        if (rel != null) InsertIntoScript($"pause while {{{rel}}}", SnippetPosition.Top);
    }

    private void SnippetActionRestart_Click(object sender, RoutedEventArgs e)
    {
        InsertIntoScript("action requires restart", SnippetPosition.End);
    }

    private void SnippetCreateConfig_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptSnippetValue("Create Config", "Enter file name (e.g. config.ini):", "config.ini");
        if (name == null) return;
        var content = PromptSnippetValue("Create Config", "Enter file content:", "[Settings]\nKey=Value");
        if (content != null) InsertIntoScript($"createfile until end_of_file\n{content}\nend_of_file\nmove __createfile \"{name}\"", SnippetPosition.BeforeInstall);
    }

    private void SnippetAppendFile_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptSnippetValue("Append File", "Enter file name:", "config.ini");
        if (name == null) return;
        var line = PromptSnippetValue("Append File", "Enter line to append:", "NewSetting=Value");
        if (line != null) InsertIntoScript($"appendfile {line}\nmove __appendfile \"{name}\"", SnippetPosition.BeforeInstall);
    }

    private enum SnippetPosition { Top, BeforeInstall, AfterInstall, End }

    private void InsertIntoScript(string snippet, SnippetPosition position)
    {
        var existing = ActionScriptBox.Text.Trim();
        var lines = existing.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        // Find the main install/uninstall command line index
        int mainCmdIndex = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.StartsWith("prefetch ", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("download ", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("waithidden msiexec", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("waithidden ", StringComparison.OrdinalIgnoreCase) && (l.Contains("/i") || l.Contains("/x") || l.Contains("uninstall")) ||
                l.StartsWith("wait msiexec", StringComparison.OrdinalIgnoreCase) ||
                l.StartsWith("run ", StringComparison.OrdinalIgnoreCase) && (l.Contains("msiexec") || l.Contains("setup")))
            {
                mainCmdIndex = i;
                break;
            }
        }

        switch (position)
        {
            case SnippetPosition.Top:
                lines.Insert(0, snippet);
                break;

            case SnippetPosition.BeforeInstall:
                if (mainCmdIndex > 0)
                    lines.Insert(mainCmdIndex, snippet);
                else
                    lines.Insert(0, snippet);
                break;

            case SnippetPosition.AfterInstall:
                if (mainCmdIndex >= 0)
                {
                    // Insert after main command (skip any continuation lines)
                    int insertAt = mainCmdIndex + 1;
                    while (insertAt < lines.Count && (lines[insertAt].StartsWith("/") || lines[insertAt].StartsWith("\"")))
                        insertAt++;
                    lines.Insert(insertAt, snippet);
                }
                else
                    lines.Add(snippet);
                break;

            case SnippetPosition.End:
                lines.Add(snippet);
                break;
        }

        ActionScriptBox.Text = string.Join(Environment.NewLine, lines);
        UpdatePreview();
    }

    private static string? PromptSnippetValue(string title, string prompt, string defaultValue)
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
        var textBox = new TextBox { Margin = new Thickness(0, 0, 0, 12) };
        textBox.Text = defaultValue ?? "";
        textBox.SelectAll();
        panel.Children.Add(textBox);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        okBtn.Click += (_, _) => { win.DialogResult = true; win.Close(); };
        var cancelBtn = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        cancelBtn.Click += (_, _) => { win.DialogResult = false; win.Close(); };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        win.Content = panel;
        textBox.Focus();

        var result = win.ShowDialog();
        if (result == true && !string.IsNullOrWhiteSpace(textBox.Text))
            return textBox.Text;
        return null;
    }

    private void RefSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshRefGrid();
    private void RefCategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshRefGrid();

    private void RefreshRefGrid()
    {
        var category = RefCategoryCombo.SelectedItem as string ?? "All";
        var search = RefSearchBox.Text.Trim();
        var commands = string.IsNullOrWhiteSpace(search)
            ? ActionScriptCommands.ByCategory(category)
            : ActionScriptCommands.ByCategory(category)
                .Where(c => c.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            c.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        RefGrid.ItemsSource = commands;
    }

    private void RefGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RefGrid.SelectedItem is ActionScriptCommands.CommandInfo cmd)
        {
            RefSyntaxBox.Text = cmd.Syntax;
            RefDescBox.Text = cmd.Description;
            RefNotesBox.Text = cmd.Notes;
            RefExampleBox.Text = cmd.Example;
        }
        else
        {
            RefSyntaxBox.Text = "";
            RefDescBox.Text = "";
            RefNotesBox.Text = "";
            RefExampleBox.Text = "";
        }
    }

    private void RefInsertSyntax_Click(object sender, RoutedEventArgs e)
    {
        if (RefGrid.SelectedItem is ActionScriptCommands.CommandInfo cmd)
        {
            var existing = ActionScriptBox.Text.Trim();
            if (string.IsNullOrEmpty(existing))
                ActionScriptBox.Text = cmd.Syntax;
            else
                ActionScriptBox.Text = existing + Environment.NewLine + Environment.NewLine + cmd.Syntax;
            UpdatePreview();
        }
    }

    private void RefInsertExample_Click(object sender, RoutedEventArgs e)
    {
        if (RefGrid.SelectedItem is ActionScriptCommands.CommandInfo cmd)
        {
            var existing = ActionScriptBox.Text.Trim();
            if (string.IsNullOrEmpty(existing))
                ActionScriptBox.Text = cmd.Example;
            else
                ActionScriptBox.Text = existing + Environment.NewLine + Environment.NewLine + cmd.Example;
            UpdatePreview();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveBtn_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            NewFixletBtn_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.O && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            BatchImportBtn_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F1)
        {
            var mainTabs = FindName("MainTabControl") as TabControl;
            if (mainTabs != null && mainTabs.Items.Count > 5)
                mainTabs.SelectedIndex = 5;
            e.Handled = true;
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        var s = AppSettings.Current;
        if (WindowState == WindowState.Normal)
        {
            s.WindowLeft = Left;
            s.WindowTop = Top;
            s.WindowWidth = Width;
            s.WindowHeight = Height;
        }
        s.Save();
    }
}
