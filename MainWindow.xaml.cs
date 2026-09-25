using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FixletBuilder.Core;
using FixletBuilder.Core.PatchFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace FixletBuilder;

public partial class MainWindow : Window
{
    private readonly List<ActionStep> _steps = new();
    private readonly List<AnalysisProperty> _analysisProps = new();
    private bool _busy;
    private bool _patchBusy;
    private bool _analysisBusy;
    private List<PatchDefinition> _patchList = new();
    private IPatchPipeline? _patchPipeline;

    public MainWindow()
    {
        InitializeComponent();
        TypeCombo.ItemsSource = FixletTemplates.Types;
        TypeCombo.SelectedIndex = 0;

        AnalysisTemplateCombo.ItemsSource = AnalysisTemplates.Keys;
        AnalysisTemplateCombo.SelectedIndex = 0;
        AnalysisPropsGrid.ItemsSource = _analysisProps;
        AnalysisPropCountText.Text = "0 properties";

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

        ClampToScreen();

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
        UpdateAnalysisPreview();

        var services = CliRunner.BuildServices();
        var winget = services.GetRequiredService<IWingetIntegration>();
        WingetStatus.Text = winget.IsAvailable() ? "winget: available" : "winget: not found";

        try
        {
            _patchPipeline = services.GetRequiredService<IPatchPipeline>();
        }
        catch
        {
            _patchPipeline = null;
        }

        PatchLogBox.Text =
            "Patch Factory ready.\r\n" +
            "  1 Discover  →  2 Validate  →  3 Generate  →  4 Publish  →  5 Groups  →  6 Deploy  →  7 Compliance\r\n" +
            "Or click Run full cycle for Patch Tuesday discover + validate + generate.\r\n" +
            "Set BigFix BaseUrl/Username/Password in appsettings.json before Publish/Deploy.\r\n";
        LoadPatchGrid();
    }

    private IPatchPipeline RequirePatchPipeline()
    {
        if (_patchPipeline is null)
        {
            var services = CliRunner.BuildServices();
            _patchPipeline = services.GetRequiredService<IPatchPipeline>();
        }
        return _patchPipeline;
    }

    private string PatchOutputDir()
    {
        var dir = PatchOutputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(dir))
            dir = "./patch-output";
        return Path.IsPathRooted(dir) ? dir : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, dir));
    }

    private string PatchesJsonPath() => Path.Combine(PatchOutputDir(), "patches.json");

    private void PatchLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
        PatchLogBox.AppendText(line);
        PatchLogBox.ScrollToEnd();
        StatusText.Text = message;
        if (message.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Discovered ", StringComparison.Ordinal) ||
            message.Contains("complete", StringComparison.OrdinalIgnoreCase))
        {
            if (PatchLogExpander is { IsExpanded: false })
                PatchLogExpander.IsExpanded = true;
        }
    }

    private void SetPatchBusy(bool busy)
    {
        _patchBusy = busy;
        foreach (var b in new[]
                 {
                     PatchDiscoverBtn, PatchValidateBtn, PatchGenerateBtn, PatchTaskBtn,
                     PatchPublishBtn, PatchGroupsBtn, PatchDeployBtn, PatchComplianceBtn, PatchCycleBtn
                 })
            b.IsEnabled = !busy;
    }

    private void ClampToScreen()
    {
        var wa = SystemParameters.WorkArea;

        MaxWidth = Math.Max(MinWidth, wa.Width);
        MaxHeight = Math.Max(MinHeight, wa.Height);

        Width = Math.Min(Width, wa.Width);
        Height = Math.Min(Height, wa.Height);

        var maxLeft = wa.Left + wa.Width - Math.Min(Width, wa.Width);
        var maxTop = wa.Top + wa.Height - Math.Min(Height, wa.Height);
        Left = Math.Max(wa.Left, Math.Min(Left, maxLeft));
        Top = Math.Max(wa.Top, Math.Min(Top, maxTop));

        if (WindowState == WindowState.Maximized)
        {
            Left = wa.Left;
            Top = wa.Top;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ClampToScreen();
        if (WindowState == WindowState.Normal && Height > SystemParameters.WorkArea.Height - 4)
            Height = SystemParameters.WorkArea.Height;
    }

    private void LoadPatchGrid()
    {
        var loaded = (List<PatchDefinition>?)null;
        try
        {
            var path = PatchesJsonPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                loaded = System.Text.Json.JsonSerializer.Deserialize<List<PatchDefinition>>(json);
            }
            else
            {
                PatchLog($"patches.json not found at {path}");
            }
        }
        catch (Exception ex)
        {
            PatchLog($"Could not load patches.json: {ex.Message}");
        }

        if (loaded is not null)
            _patchList = loaded;
        else if (_patchList is null)
            _patchList = new List<PatchDefinition>();

        ApplyPatchFilter();
    }

    private void ApplyPatchFilter()
    {
        var all = _patchList ?? new List<PatchDefinition>();
        var q = PatchFilterBox?.Text?.Trim() ?? "";
        List<PatchDefinition> view;
        if (q.Length == 0)
            view = all;
        else
            view = all.Where(p =>
                (p.Kb?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.Product?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

        PatchGrid.ItemsSource = null;
        PatchGrid.ItemsSource = view;
        PatchGrid.Items.Refresh();
        PatchCountText.Text = q.Length == 0
            ? $"{view.Count} patch(es)"
            : $"{view.Count} of {all.Count} match \"{q}\"";
    }

    private void PatchFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (PatchGrid is null || _patchList is null) return;
        ApplyPatchFilter();
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabControl is null || FixletToolbar is null || FixletMetaPanel is null) return;
        if (e.Source is not TabControl) return;

        var header = (MainTabControl.SelectedItem as TabItem)?.Header?.ToString();
        var hideFixletChrome = header is "Patch Factory" or "Analysis";

        FixletToolbar.Visibility = hideFixletChrome ? Visibility.Collapsed : Visibility.Visible;
        FixletMetaPanel.Visibility = hideFixletChrome ? Visibility.Collapsed : Visibility.Visible;
        FixletMetaPanel.Margin = hideFixletChrome ? new Thickness(0) : new Thickness(0, 8, 0, 0);
    }

    private async void PatchDiscoverBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_patchBusy) return;
        SetPatchBusy(true);
        try
        {
            var kbs = PatchKbBox.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
            var win10 = PatchWin10Check.IsChecked == true;
            var win11 = PatchWin11Check.IsChecked == true;
            var month = PatchTuesdayCheck.IsChecked == true;

            var request = new DiscoverRequest
            {
                KbNumbers = kbs,
                Query = string.IsNullOrWhiteSpace(PatchQueryBox.Text) ? null : PatchQueryBox.Text.Trim(),
                ProductFilter = string.IsNullOrWhiteSpace(PatchProductBox.Text) ? null : PatchProductBox.Text.Trim(),
                ScanWindows10 = win10,
                ScanWindows11 = win11,
                ScanThisMonth = month,
                PatchTuesday = month && kbs.Count == 0 &&
                               string.IsNullOrWhiteSpace(PatchQueryBox.Text) &&
                               string.IsNullOrWhiteSpace(PatchProductBox.Text),
                Architecture = (PatchArchCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "x64",
                MaxResults = 80
            };

            var wantsScan = request.WantsMonthScan || win10 || win11;
            if (wantsScan)
                request.ReleasedAfter = UpdateCatalogClient.LastPatchTuesday().AddDays(-1);

            if (kbs.Count == 0 && request.Query is null && request.ProductFilter is null &&
                !wantsScan)
            {
                PatchLog("Enter KB(s)/query/product, or keep Win10/Win11/Whole month checked.");
                return;
            }

            PatchLog($"Discovering… arch={request.Architecture}");
            var outputDir = PatchOutputDir();
            var pipeline = RequirePatchPipeline();
            Directory.CreateDirectory(outputDir);
            var patches = await Task.Run(() =>
                pipeline.DiscoverAsync(request, outputDir));
            _patchList = patches ?? new List<PatchDefinition>();
            ApplyPatchFilter();
            if (patches is null || patches.Count == 0)
            {
                PatchLog("No patches matched. Check Win10/Win11/Whole month filters, or enter a KB.");
                MessageBox.Show(this,
                    "No patches found in the Microsoft Update Catalog for this scan.\n\n" +
                    "Tips:\n" +
                    "• Keep Win10 + Win11 + Whole month checked\n" +
                    "• Or paste a KB number (e.g. KB5122880)\n" +
                    "• Or use a free-text catalog query",
                    "Discover", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            PatchLog($"Discovered {patches.Count} patch(es) → {PatchesJsonPath()}");
        }
        catch (Exception ex)
        {
            PatchLog("Discover failed: " + ex.Message);
            PatchLog(ex.ToString());
            MessageBox.Show(this, "Discover failed:\n" + ex.Message, "Discover",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetPatchBusy(false);
        }
    }

    private async void PatchValidateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_patchBusy) return;
        if (!File.Exists(PatchesJsonPath()))
        {
            PatchLog("No patches.json — run Discover first.");
            return;
        }

        SetPatchBusy(true);
        try
        {
            var download = PatchDownloadCheck.IsChecked == true;
            PatchLog(download ? "Validating (with download)…" : "Validating (offline)…");
            var patchesPath = PatchesJsonPath();
            var pipeline = RequirePatchPipeline();
            var result = await Task.Run(() =>
                pipeline.ValidateAsync(patchesPath, download));

            foreach (var report in result.Reports)
            {
                PatchLog($"{report.Kb}: {(report.Passed ? "PASS" : "FAIL")}");
                foreach (var check in report.Checks.Where(c => !c.Passed || c.Message.Contains("profile", StringComparison.OrdinalIgnoreCase)))
                    if (!check.Passed)
                        PatchLog($"    FAIL {check.Name}: {check.Message}");
            }

            PatchLog(result.AllPassed
                ? $"Validation passed ({result.Reports.Count} patch(es))."
                : $"Validation FAILED — {result.FailedCount} patch(es) blocked.");
            LoadPatchGrid();
            if (!result.AllPassed)
                MessageBox.Show(this, $"{result.FailedCount} patch(es) failed validation. See log.",
                    "Validation gate", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            PatchLog("Validate failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Validate", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetPatchBusy(false);
        }
    }

    private async void PatchGenerateBtn_Click(object sender, RoutedEventArgs e) =>
        await RunPatchGenerateAsync(asTask: false);

    private async void PatchTaskBtn_Click(object sender, RoutedEventArgs e) =>
        await RunPatchGenerateAsync(asTask: true);

    private async Task RunPatchGenerateAsync(bool asTask)
    {
        if (_patchBusy) return;
        if (!File.Exists(PatchesJsonPath()))
        {
            PatchLog("No patches.json — run Discover first.");
            return;
        }

        SetPatchBusy(true);
        try
        {
            var outDir = Path.Combine(PatchOutputDir(), "bes");
            var patchesPath = PatchesJsonPath();
            var pipeline = RequirePatchPipeline();
            PatchLog(asTask ? "Generating Tasks…" : "Generating Fixlets…");
            await Task.Run(() => pipeline.GenerateAsync(patchesPath, outDir, asTask));
            LoadPatchGrid();
            PatchLog($"Content written to {outDir}");
        }
        catch (Exception ex)
        {
            PatchLog("Generate failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Generate", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetPatchBusy(false);
        }
    }

    private async void PatchPublishBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_patchBusy) return;
        if (!File.Exists(PatchesJsonPath()))
        {
            PatchLog("No patches.json — run Discover + Generate first.");
            return;
        }

        SetPatchBusy(true);
        try
        {
            var whatIf = PatchWhatIfCheck.IsChecked == true;
            var site = PatchSiteBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(site)) site = "Enterprise Windows Patching";
            PatchLog(whatIf ? "Publish (what-if)…" : $"Publishing to custom site '{site}'…");
            var patchesPath = PatchesJsonPath();
            var outputDir = PatchOutputDir();
            var pipeline = RequirePatchPipeline();
            var code = await Task.Run(() =>
                pipeline.PublishAsync(patchesPath, outputDir, "custom", site, whatIf));
            LoadPatchGrid();
            PatchLog(code == 0 ? "Publish finished." : "Publish finished with errors — see log.");
        }
        catch (Exception ex)
        {
            PatchLog("Publish failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Publish", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetPatchBusy(false);
        }
    }

    private async void PatchGroupsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_patchBusy) return;
        SetPatchBusy(true);
        try
        {
            var whatIf = PatchWhatIfCheck.IsChecked == true;
            PatchLog(whatIf ? "Groups (what-if)…" : "Ensuring automatic groups…");
            var patchesPath = PatchesJsonPath();
            var outputDir = PatchOutputDir();
            var pipeline = RequirePatchPipeline();
            await Task.Run(() =>
                pipeline.EnsureGroupsAsync(patchesPath, outputDir, "", whatIf));
            PatchLog("Groups step finished.");
        }
        catch (Exception ex)
        {
            PatchLog("Groups failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Groups", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetPatchBusy(false);
        }
    }

    private async void PatchDeployBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_patchBusy) return;
        var stage = (PatchStageCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "pilot";
        var whatIf = PatchWhatIfCheck.IsChecked == true;

        if (!whatIf)
        {
            var confirm = MessageBox.Show(this,
                $"Deploy all patches in this cycle to stage '{stage}'?\n\n" +
                "Ensure pilot soak has completed before wave stages.",
                "Confirm deploy", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
        }

        SetPatchBusy(true);
        try
        {
            PatchLog(whatIf ? $"Deploy {stage} (what-if)…" : $"Deploying to {stage}…");
            var patchesPath = PatchesJsonPath();
            var outputDir = PatchOutputDir();
            var pipeline = RequirePatchPipeline();
            var code = await Task.Run(() =>
                pipeline.DeployAsync(patchesPath, outputDir, stage, whatIf));
            PatchLog(code == 0 ? $"Deploy {stage} finished." : $"Deploy {stage} had errors.");
        }
        catch (Exception ex)
        {
            PatchLog("Deploy failed: " + ex.Message);
            PatchLog(ex.ToString());
            MessageBox.Show(this, "Deploy failed:\n" + ex.Message, "Deploy",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetPatchBusy(false);
        }
    }

    private async void PatchComplianceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_patchBusy) return;
        SetPatchBusy(true);
        try
        {
            PatchLog("Running compliance…");
            var outputDir = PatchOutputDir();
            var patchesPath = PatchesJsonPath();
            var reportPath = Path.Combine(outputDir, "compliance.json");
            var pipeline = RequirePatchPipeline();
            await Task.Run(() =>
                pipeline.ComplianceAsync(patchesPath, outputDir, reportPath));
            PatchLog($"Compliance report: {reportPath}");
        }
        catch (Exception ex)
        {
            PatchLog("Compliance failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Compliance", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetPatchBusy(false);
        }
    }

    private async void PatchCycleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_patchBusy) return;
        SetPatchBusy(true);
        try
        {
            var pipeline = RequirePatchPipeline();
            var kbs = PatchKbBox.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim()).Where(k => k.Length > 0).ToList();

            var win10 = PatchWin10Check.IsChecked == true;
            var win11 = PatchWin11Check.IsChecked == true;
            var month = PatchTuesdayCheck.IsChecked == true;

            var request = new DiscoverRequest
            {
                KbNumbers = kbs,
                Query = string.IsNullOrWhiteSpace(PatchQueryBox.Text) ? null : PatchQueryBox.Text.Trim(),
                ProductFilter = string.IsNullOrWhiteSpace(PatchProductBox.Text) ? null : PatchProductBox.Text.Trim(),
                ScanWindows10 = win10,
                ScanWindows11 = win11,
                ScanThisMonth = month,
                Architecture = (PatchArchCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "x64",
                MaxResults = 80
            };

            if (kbs.Count == 0 && request.Query is null && request.ProductFilter is null &&
                (win10 || win11 || month))
            {
                request.PatchTuesday = true;
                request.ReleasedAfter = UpdateCatalogClient.LastPatchTuesday().AddDays(-1);
            }

            PatchLog("Cycle: discover…");
            var outputDir = PatchOutputDir();
            var patches = await Task.Run(() => pipeline.DiscoverAsync(request, outputDir));
            _patchList = patches;
            LoadPatchGrid();

            if (patches.Count == 0)
            {
                PatchLog("Cycle: no patches discovered.");
                return;
            }

            var patchesPath = PatchesJsonPath();
            var download = PatchDownloadCheck.IsChecked == true;
            PatchLog("Cycle: validate…");
            var validation = await Task.Run(() =>
                pipeline.ValidateAsync(patchesPath, download));
            foreach (var r in validation.Reports)
                PatchLog($"  {r.Kb}: {(r.Passed ? "PASS" : "FAIL")}");

            if (!validation.AllPassed)
            {
                PatchLog("Cycle stopped at validation gate.");
                MessageBox.Show(this, "Validation gate failed — not generating content.",
                    "Patch cycle", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PatchLog("Cycle: generate…");
            await Task.Run(() =>
                pipeline.GenerateAsync(patchesPath, Path.Combine(outputDir, "bes"), false));
            LoadPatchGrid();
            PatchLog("Cycle complete. Review BES, then Publish → Groups → Deploy.");
        }
        catch (Exception ex)
        {
            PatchLog("Cycle failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Patch cycle", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetPatchBusy(false);
        }
    }

    private void PatchOpenFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = PatchOutputDir();
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            PatchLog("Open folder failed: " + ex.Message);
        }
    }

    private void PatchGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PatchGrid.SelectedItem is not PatchDefinition p) return;
        PatchLog($"Selected {p.Kb}: {p.Title}");
        if (!string.IsNullOrWhiteSpace(p.GeneratedBesPath) && File.Exists(p.GeneratedBesPath))
        {
            try
            {
                DescriptionBox.Text = p.Title;
                SourceIdBox.Text = p.Kb;
                SourceBox.Text = "Microsoft";
                CategoryBox.Text = "Security Updates";
                SourceUrlBox.Text = p.DownloadUrl;
                Sha1Box.Text = p.Sha1;
                Sha256Box.Text = p.Sha256;
                FileSizeBox.Text = p.Size.ToString();
                UpdatePreview();
            }
            catch
            {
                // form fill is best-effort
            }
        }
    }

    // ─── Relevance detection method ────────────────────────────────────────

    private DetectionMethod SelectedDetectionMethod()
    {
        if (DetMsiRadio.IsChecked == true) return DetectionMethod.MsiProductCode;
        if (DetFileRadio.IsChecked == true) return DetectionMethod.FileVersion;
        if (DetRegRadio.IsChecked == true) return DetectionMethod.RegistryKey;
        if (DetNameRadio.IsChecked == true) return DetectionMethod.DisplayNameContains;
        if (DetPathRadio.IsChecked == true) return DetectionMethod.FileOrFolderExists;
        return DetectionMethod.Auto;
    }

    private DetectionInput BuildDetectionInput(string type) => new()
    {
        Type = type,
        Method = SelectedDetectionMethod(),
        AppName = AppNameBox.Text.Trim(),
        Version = VersionBox.Text.Trim(),
        InstallPath = InstallPathBox.Text.Trim(),
        RegistryKeyPath = RegistryKeyBox.Text.Trim(),
        RegistryValueName = string.IsNullOrWhiteSpace(RegistryValueNameBox.Text)
            ? "DisplayVersion"
            : RegistryValueNameBox.Text.Trim(),
        RegistryValue = RegistryValueBox.Text.Trim(),
        MsiProductCode = MsiCodeBox.Text.Trim()
    };

    private void DetMethod_Changed(object sender, RoutedEventArgs e)
    {
        if (DetHintText is null || !IsLoaded) return;
        DetHintText.Text = DetectionMethods.PriorityHint(SelectedDetectionMethod());
    }

    private void GenerateRelevanceBtn_Click(object sender, RoutedEventArgs e)
    {
        var type = TypeCombo.SelectedItem as string ?? FixletTemplates.TypeInstall;
        var result = RelevanceBuilder.BuildDetection(BuildDetectionInput(type));

        RelevanceBox.Text = string.Join(Environment.NewLine, result.Relevance);
        SuccessBox.Text = result.SuccessCriteria;
        UpdatePreview();

        StatusText.Text =
            $"Relevance generated using {DetectionMethods.Label(result.Method)} - " +
            $"{result.Relevance.Count} expression(s)." +
            (result.Warning is null ? "" : " " + result.Warning);
    }

    private void InsertNotInstalledOrOlderBtn_Click(object sender, RoutedEventArgs e)
    {
        var appName = AppNameBox.Text.Trim();
        if (appName.Length == 0)
        {
            StatusText.Text = "Enter an application name first - it is used as the DisplayName to search for.";
            return;
        }

        var version = VersionBox.Text.Trim();
        var line =
            "not exists keys whose (" +
            "exists value \"DisplayName\" of it and " +
            $"value \"DisplayName\" of it as string = \"{appName}\"" +
            (version.Length > 0
                ? $" and exists value \"DisplayVersion\" of it and value \"DisplayVersion\" of it as string as version >= \"{version}\" as version"
                : "") +
            ") of keys \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\" of native registry";

        var existing = RelevanceBox.Text.Trim();
        RelevanceBox.Text = existing.Length == 0 ? line : existing + Environment.NewLine + line;
        UpdatePreview();
        StatusText.Text = "Combined 'not installed OR below required version' expression appended.";
    }

    private void ApplyTemplateBtn_Click(object sender, RoutedEventArgs e)
    {
        var type = TypeCombo.SelectedItem as string ?? FixletTemplates.TypeInstall;
        var sha1 = Sha1Box.Text.Trim();
        var sha256 = Sha256Box.Text.Trim();
        long.TryParse(FileSizeBox.Text.Trim(), out long fileSizeBytes);
        var processToKill = ProcessNameBox.Text.Trim();
        var serviceToStop = ServiceNameBox.Text.Trim();

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

        var detection = RelevanceBuilder.BuildDetection(BuildDetectionInput(type));

        TitleBox.Text = t.Title;
        RelevanceBox.Text = string.Join(Environment.NewLine, detection.Relevance);
        DescriptionBox.Text = t.Description;
        ActionScriptBox.Text = t.ActionScript;
        SuccessBox.Text = detection.SuccessCriteria;

        BuildDefaultSteps(type);
        UpdatePreview();
        StatusText.Text = $"Template applied ({DetectionMethods.Label(detection.Method)} detection). Use the Action Builder buttons to adjust steps.";
    }

    private void NewFixletBtn_Click(object sender, RoutedEventArgs e)
    {
        TypeCombo.SelectedIndex = 0;
        foreach (var b in new[]
                 {
                     AppNameBox, VersionBox, SourceUrlBox, SilentArgsBox, InstallPathBox, UninstallStringBox,
                     RegistryKeyBox, RegistryValueBox, MsiCodeBox,
                     ProcessNameBox, ServiceNameBox,
                     TitleBox, SourceBox, SourceIdBox, DomainBox, DownloadSizeBox,
                     Sha1Box, Sha256Box, FileSizeBox,
                     RelevanceBox, DescriptionBox, SuccessBox
                 })
            b.Clear();
        CategoryBox.Text = "Applications";
        RegistryValueNameBox.Text = "DisplayVersion";
        DetAutoRadio.IsChecked = true;
        DetHintText.Text = DetectionMethods.PriorityHint(DetectionMethod.Auto);
        _steps.Clear();
        if (RawEditCheck.IsChecked == true) RawEditCheck.IsChecked = false;
        ActionScriptBox.Text = "";

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
        if (_busy) return;
        _busy = true;
        try
        {
            UpdateAnalysisPreview();

            if (RawEditCheck.IsChecked != true)
                RenderStepsToScript();
            RefreshStepsList();

            var model = BuildModel();
            var writer = new FixletWriter(Microsoft.Extensions.Logging.Abstractions.NullLogger<FixletWriter>.Instance);
            XmlHighlight.Apply(XmlPreviewBox, writer.GenerateXml(model));
            CountsText.Text =
                $"Steps: {_steps.Count}  |  Relevance: {model.Relevance.Count} expression(s)  |  " +
                $"Script: {(model.ActionScript.Length == 0 ? 0 : model.ActionScript.Split('\n').Length)} lines" +
                $"  |  Analysis props: {_analysisProps.Count}";
        }
        finally
        {
            _busy = false;
        }
    }

    // ─── Action Builder: step list + auto-generated script ───────────────

    private FormSnapshot Snapshot() => new()
    {
        AppName = AppNameBox.Text.Trim(),
        Url = SourceUrlBox.Text.Trim(),
        Sha1 = Sha1Box.Text.Trim(),
        Sha256 = Sha256Box.Text.Trim(),
        FileSize = FileSizeBox.Text.Trim(),
        SilentArgs = SilentArgsBox.Text.Trim(),
        InstallPath = InstallPathBox.Text.Trim(),
        UninstallString = UninstallStringBox.Text.Trim()
    };

    private void BuildDefaultSteps(string type)
    {
        _steps.Clear();
        var snap = Snapshot();
        var url = snap.Url;
        var hasDownload = !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(snap.Sha1);

        if (type is FixletTemplates.TypeInstall or FixletTemplates.TypeUpgrade)
        {
            if (hasDownload)
                _steps.Add(ActionStep.Create("download"));
            if (!string.IsNullOrWhiteSpace(ProcessNameBox.Text.Trim()))
                _steps.Add(ActionStep.Create("kill", ("process", ProcessNameBox.Text.Trim())));
            if (!string.IsNullOrWhiteSpace(ServiceNameBox.Text.Trim()))
                _steps.Add(ActionStep.Create("stop", ("service", ServiceNameBox.Text.Trim())));

            var isMsi = ActionScriptCommands.IsMsiUrl(url) ||
                        (!string.IsNullOrWhiteSpace(snap.InstallPath) &&
                         snap.InstallPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
            _steps.Add(ActionStep.Create("install",
                ("mode", isMsi ? "msi" : "exe"),
                ("url", url),
                ("path", snap.InstallPath),
                ("args", snap.SilentArgs)));
            _steps.Add(ActionStep.Create("actionRestart"));
        }
        else if (type == FixletTemplates.TypeUninstall)
        {
            if (!string.IsNullOrWhiteSpace(ProcessNameBox.Text.Trim()))
                _steps.Add(ActionStep.Create("kill", ("process", ProcessNameBox.Text.Trim())));
            if (!string.IsNullOrWhiteSpace(ServiceNameBox.Text.Trim()))
                _steps.Add(ActionStep.Create("stop", ("service", ServiceNameBox.Text.Trim())));
            _steps.Add(ActionStep.Create("uninstall", ("cmd", snap.UninstallString)));
            _steps.Add(ActionStep.Create("actionRestart"));
        }
        // custom: leave empty — user builds with buttons
    }

    private void RenderStepsToScript()
    {
        if (_steps.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(ActionScriptBox.Text))
                ActionScriptBox.Text = "// No steps yet — use the buttons above to build the action.";
            return;
        }

        var snap = Snapshot();
        var lines = new List<string>();
        foreach (var step in _steps)
        {
            var rendered = step.Render(snap);
            if (!string.IsNullOrWhiteSpace(rendered))
                lines.Add(rendered);
        }

        // Ensure client restart is last if present anywhere but the end
        var clientRestartIdx = lines.FindIndex(l =>
            l.Equals("client restart", StringComparison.OrdinalIgnoreCase));
        if (clientRestartIdx >= 0 && clientRestartIdx < lines.Count - 1)
        {
            var line = lines[clientRestartIdx];
            lines.RemoveAt(clientRestartIdx);
            lines.Add(line);
        }

        var text = string.Join(Environment.NewLine, lines);
        if (ActionScriptBox.Text != text)
            ActionScriptBox.Text = text;
    }

    private void RefreshStepsList()
    {
        StepsList.ItemsSource = null;
        StepsList.ItemsSource = _steps;
        StepsEmptyHint.Visibility = _steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddStep(ActionStep step)
    {
        if (RawEditCheck.IsChecked == true)
            RawEditCheck.IsChecked = false;
        _steps.Add(step);
        UpdatePreview();
        StatusText.Text = $"Step added: {step.Label}";
    }

    private void StepDownload_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceUrlBox.Text.Trim()))
        {
            MessageBox.Show(this, "Enter an Installer URL on the Application panel first.", "Download",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(Sha1Box.Text.Trim()))
        {
            var go = MessageBox.Show(this,
                "SHA1 is missing. The prefetch line will be incomplete.\n\nClick Fetch Hashes first, or add the step anyway?",
                "Missing hash", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (go != MessageBoxResult.Yes) return;
        }
        // Replace existing download step rather than duplicating
        _steps.RemoveAll(s => s.Kind == "download");
        _steps.Insert(0, ActionStep.Create("download"));
        UpdatePreview();
        StatusText.Text = "Download (prefetch) step added.";
    }

    private void StepKill_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptSnippetValue("Kill Process", "Enter process name to kill:", "notepad");
        if (name != null) AddStep(ActionStep.Create("kill", ("process", name)));
    }

    private void StepStop_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptSnippetValue("Stop Service", "Enter service name to stop:", "W3SVC");
        if (name != null) AddStep(ActionStep.Create("stop", ("service", name)));
    }

    private void StepDeleteFile_Click(object sender, RoutedEventArgs e)
    {
        var path = PromptSnippetValue("Delete File", "Enter file path to delete:", @"C:\temp\file.exe");
        if (path != null) AddStep(ActionStep.Create("deleteFile", ("path", path)));
    }

    private void StepDeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = PromptSnippetValue("Delete Folder", "Enter folder path to delete:", @"C:\temp");
        if (path != null) AddStep(ActionStep.Create("deleteFolder", ("path", path)));
    }

    private void StepCopy_Click(object sender, RoutedEventArgs e)
    {
        var src = PromptSnippetValue("Copy File", "Enter source file path:", @"C:\source\file.txt");
        if (src == null) return;
        var dst = PromptSnippetValue("Copy File", "Enter destination path:", @"C:\dest\file.txt");
        if (dst == null) return;
        AddStep(ActionStep.Create("copy", ("src", src), ("dst", dst)));
    }

    private void StepMove_Click(object sender, RoutedEventArgs e)
    {
        var src = PromptSnippetValue("Move File", "Enter source file path:", @"C:\old.exe");
        if (src == null) return;
        var dst = PromptSnippetValue("Move File", "Enter destination path:", @"C:\new.exe");
        if (dst == null) return;
        AddStep(ActionStep.Create("move", ("src", src), ("dst", dst)));
    }

    private void StepMkdir_Click(object sender, RoutedEventArgs e)
    {
        var path = PromptSnippetValue("Create Folder", "Enter folder path to create:", @"C:\Program Files\MyApp");
        if (path != null) AddStep(ActionStep.Create("mkdir", ("path", path)));
    }

    private void StepRegset_Click(object sender, RoutedEventArgs e)
    {
        var key = PromptSnippetValue("Set Registry", "Enter registry key (without brackets):", @"HKLM\SOFTWARE\MyApp");
        if (key == null) return;
        var name = PromptSnippetValue("Set Registry", "Enter value name:", "Installed");
        if (name == null) return;
        var val = PromptSnippetValue("Set Registry", "Enter value data:", "1");
        if (val != null) AddStep(ActionStep.Create("regset", ("key", key), ("name", name), ("value", val)));
    }

    private void StepRegset64_Click(object sender, RoutedEventArgs e)
    {
        var key = PromptSnippetValue("Set Reg 64", "Enter registry key (without brackets):", @"HKLM\SOFTWARE\MyApp");
        if (key == null) return;
        var name = PromptSnippetValue("Set Reg 64", "Enter value name:", "Installed");
        if (name == null) return;
        var val = PromptSnippetValue("Set Reg 64", "Enter value data:", "1");
        if (val != null) AddStep(ActionStep.Create("regset64", ("key", key), ("name", name), ("value", val)));
    }

    private void StepRegdelete_Click(object sender, RoutedEventArgs e)
    {
        var key = PromptSnippetValue("Delete Registry Value", "Enter registry key (without brackets):", @"HKLM\SOFTWARE\MyApp");
        if (key == null) return;
        var name = PromptSnippetValue("Delete Registry Value", "Enter value name to delete:", "OldValue");
        if (name != null) AddStep(ActionStep.Create("regdelete", ("key", key), ("name", name)));
    }

    private void StepRegkeydelete_Click(object sender, RoutedEventArgs e)
    {
        var key = PromptSnippetValue("Delete Registry Key", "Enter registry key to delete (without brackets):", @"HKLM\SOFTWARE\MyApp");
        if (key != null) AddStep(ActionStep.Create("regkeydelete", ("key", key)));
    }

    private void StepInstallMsi_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceUrlBox.Text.Trim()) && string.IsNullOrWhiteSpace(InstallPathBox.Text.Trim()))
        {
            MessageBox.Show(this, "Enter an Installer URL (or Install path) first.", "Install MSI",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var args = PromptSnippetValue("Install MSI", "Silent args (default /qn):", "/qn") ?? "/qn";
        _steps.Add(ActionStep.Create("install", ("mode", "msi"), ("args", args)));
        UpdatePreview();
        StatusText.Text = "Install MSI step added.";
    }

    private void StepInstallExe_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceUrlBox.Text.Trim()) && string.IsNullOrWhiteSpace(InstallPathBox.Text.Trim()))
        {
            MessageBox.Show(this, "Enter an Installer URL (or Install path) first.", "Install EXE",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var def = SilentArgsBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(def)) def = "/S";
        var args = PromptSnippetValue("Install EXE", "Silent args:", def) ?? def;
        _steps.Add(ActionStep.Create("install", ("mode", "exe"), ("args", args)));
        UpdatePreview();
        StatusText.Text = "Install EXE step added.";
    }

    private void StepInstallLocal_Click(object sender, RoutedEventArgs e)
    {
        var def = InstallPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(def)) def = @"C:\Path\To\installer.exe";
        var path = PromptSnippetValue("Install Local File", "Path to installer (msi or exe):", def);
        if (path == null) return;
        var isMsi = path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
        var defArgs = isMsi ? "/qn" : "/S";
        if (!string.IsNullOrWhiteSpace(SilentArgsBox.Text.Trim())) defArgs = SilentArgsBox.Text.Trim();
        var args = PromptSnippetValue("Install Local File", "Silent args:", defArgs) ?? defArgs;
        _steps.Add(ActionStep.Create("install", ("mode", isMsi ? "msi" : "exe"), ("path", path), ("args", args), ("url", "")));
        UpdatePreview();
        StatusText.Text = "Local install step added.";
    }

    private void StepUninstall_Click(object sender, RoutedEventArgs e)
    {
        var def = UninstallStringBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(def)) def = @"msiexec /x {GUID}";
        var cmd = PromptSnippetValue("Run Uninstall", "Uninstall command:", def);
        if (cmd == null) return;
        var args = PromptSnippetValue("Run Uninstall", "Extra args (blank for none):", "") ?? "";
        _steps.Add(ActionStep.Create("uninstall", ("cmd", cmd), ("args", args)));
        UpdatePreview();
        StatusText.Text = "Uninstall step added.";
    }

    private void StepRun_Click(object sender, RoutedEventArgs e)
    {
        var cmd = PromptSnippetValue("Run Command", "Command to run (hidden, waits for exit):", "notepad.exe");
        if (cmd != null) AddStep(ActionStep.Create("wait", ("cmd", cmd)));
    }

    private void StepWait_Click(object sender, RoutedEventArgs e)
    {
        var cmd = PromptSnippetValue("Wait Command", "Command:", "setup.exe /silent");
        if (cmd != null) AddStep(ActionStep.Create("wait", ("cmd", cmd)));
    }

    private void StepRestart_Click(object sender, RoutedEventArgs e)
    {
        var delay = PromptSnippetValue("Restart", "Delay in seconds (0 = immediate):", "180");
        if (delay != null) AddStep(ActionStep.Create("restart", ("delay", delay)));
    }

    private void StepShutdown_Click(object sender, RoutedEventArgs e)
    {
        var delay = PromptSnippetValue("Shutdown", "Delay in seconds (0 = immediate):", "60");
        if (delay != null) AddStep(ActionStep.Create("shutdown", ("delay", delay)));
    }

    private void StepActionRestart_Click(object sender, RoutedEventArgs e) =>
        AddStep(ActionStep.Create("actionRestart"));

    private void StepClientRestart_Click(object sender, RoutedEventArgs e) =>
        AddStep(ActionStep.Create("clientRestart"));

    private void StepForceRefresh_Click(object sender, RoutedEventArgs e) =>
        AddStep(ActionStep.Create("forceRefresh"));

    private void StepContinueIf_Click(object sender, RoutedEventArgs e)
    {
        var rel = PromptSnippetValue("Continue If", "Relevance condition:", @"name of operating system = ""Win10""")
                  ?? "true";
        AddStep(ActionStep.Create("continueIf", ("relevance", rel)));
    }

    private void StepPauseWhile_Click(object sender, RoutedEventArgs e)
    {
        var rel = PromptSnippetValue("Pause While", "Relevance condition:", @"exists running application ""updater.exe""")
                  ?? "true";
        AddStep(ActionStep.Create("pauseWhile", ("relevance", rel)));
    }

    private void StepCreateConfig_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptSnippetValue("Create Config", "File name (e.g. config.ini):", "config.ini");
        if (name == null) return;
        var content = PromptSnippetValue("Create Config", "File content:", "[Settings]\nKey=Value");
        if (content != null) AddStep(ActionStep.Create("createConfig", ("file", name), ("content", content)));
    }

    private void StepAppendFile_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptSnippetValue("Append File", "File name:", "config.ini");
        if (name == null) return;
        var line = PromptSnippetValue("Append File", "Line to append:", "NewSetting=Value");
        if (line != null) AddStep(ActionStep.Create("appendFile", ("file", name), ("line", line)));
    }

    private void StepCustom_Click(object sender, RoutedEventArgs e)
    {
        var line = PromptSnippetValue("Custom Line", "Action script line:", "wait cmd.exe /c echo hello");
        if (line != null) AddStep(ActionStep.Create("custom", ("line", line)));
    }

    private void StepRemove_Click(object sender, RoutedEventArgs e)
    {
        if (StepsList.SelectedItem is not ActionStep step)
        {
            StatusText.Text = "Select a step to remove.";
            return;
        }
        _steps.Remove(step);
        UpdatePreview();
        StatusText.Text = "Step removed.";
    }

    private void StepMoveUp_Click(object sender, RoutedEventArgs e) => MoveStep(-1);

    private void StepMoveDown_Click(object sender, RoutedEventArgs e) => MoveStep(1);

    private void MoveStep(int delta)
    {
        if (StepsList.SelectedItem is not ActionStep step) return;
        int idx = _steps.IndexOf(step);
        int newIdx = idx + delta;
        if (idx < 0 || newIdx < 0 || newIdx >= _steps.Count) return;
        _steps.RemoveAt(idx);
        _steps.Insert(newIdx, step);
        UpdatePreview();
        StepsList.SelectedItem = step;
        StepsList.ScrollIntoView(step);
    }

    private void StepClear_Click(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0) return;
        var confirm = MessageBox.Show(this, "Remove all action steps?", "Clear steps",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        _steps.Clear();
        if (RawEditCheck.IsChecked == true) RawEditCheck.IsChecked = false;
        ActionScriptBox.Text = "";
        UpdatePreview();
        StatusText.Text = "All steps cleared.";
    }

    private void StepsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (StepsList.SelectedItem is not ActionStep step) return;
        EditStep(step);
    }

    private void EditStep(ActionStep step)
    {
        bool changed = false;
        switch (step.Kind)
        {
            case "kill":
            {
                var v = PromptSnippetValue("Edit Kill Process", "Process name:", step.Get("process"));
                if (v != null) { step.Params["process"] = v; changed = true; }
                break;
            }
            case "stop":
            {
                var v = PromptSnippetValue("Edit Stop Service", "Service name:", step.Get("service"));
                if (v != null) { step.Params["service"] = v; changed = true; }
                break;
            }
            case "deleteFile":
            case "deleteFolder":
            case "mkdir":
            {
                var v = PromptSnippetValue("Edit Step", "Path:", step.Get("path"));
                if (v != null) { step.Params["path"] = v; changed = true; }
                break;
            }
            case "copy":
            case "move":
            {
                var src = PromptSnippetValue("Edit Step", "Source:", step.Get("src"));
                if (src == null) break;
                var dst = PromptSnippetValue("Edit Step", "Destination:", step.Get("dst"));
                if (dst == null) break;
                step.Params["src"] = src;
                step.Params["dst"] = dst;
                changed = true;
                break;
            }
            case "regset":
            case "regset64":
            {
                var key = PromptSnippetValue("Edit Step", "Registry key:", step.Get("key"));
                if (key == null) break;
                var name = PromptSnippetValue("Edit Step", "Value name:", step.Get("name"));
                if (name == null) break;
                var val = PromptSnippetValue("Edit Step", "Value data:", step.Get("value"));
                if (val == null) break;
                step.Params["key"] = key;
                step.Params["name"] = name;
                step.Params["value"] = val;
                changed = true;
                break;
            }
            case "regdelete":
            {
                var key = PromptSnippetValue("Edit Step", "Registry key:", step.Get("key"));
                if (key == null) break;
                var name = PromptSnippetValue("Edit Step", "Value name:", step.Get("name"));
                if (name == null) break;
                step.Params["key"] = key;
                step.Params["name"] = name;
                changed = true;
                break;
            }
            case "regkeydelete":
            {
                var key = PromptSnippetValue("Edit Step", "Registry key:", step.Get("key"));
                if (key != null) { step.Params["key"] = key; changed = true; }
                break;
            }
            case "install":
            {
                var def = step.Get("args");
                if (string.IsNullOrWhiteSpace(def)) def = SilentArgsBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(def)) def = ActionScriptCommands.IsMsiUrl(SourceUrlBox.Text.Trim()) ? "/qn" : "/S";
                var args = PromptSnippetValue("Edit Install", "Silent args:", def);
                if (args != null) { step.Params["args"] = args; changed = true; }
                break;
            }
            case "uninstall":
            {
                var def = step.Get("cmd");
                if (string.IsNullOrWhiteSpace(def)) def = UninstallStringBox.Text.Trim();
                var cmd = PromptSnippetValue("Edit Uninstall", "Uninstall command:", def);
                if (cmd != null) { step.Params["cmd"] = cmd; changed = true; }
                break;
            }
            case "run":
            case "wait":
            {
                var v = PromptSnippetValue("Edit Command", "Command:", step.Get("cmd"));
                if (v != null) { step.Params["cmd"] = v; changed = true; }
                break;
            }
            case "restart":
            case "shutdown":
            {
                var v = PromptSnippetValue("Edit Step", "Delay in seconds:", step.Get("delay"));
                if (v != null) { step.Params["delay"] = v; changed = true; }
                break;
            }
            case "continueIf":
            case "pauseWhile":
            {
                var v = PromptSnippetValue("Edit Step", "Relevance condition:", step.Get("relevance"));
                if (v != null) { step.Params["relevance"] = v; changed = true; }
                break;
            }
            case "createConfig":
            {
                var file = PromptSnippetValue("Edit Step", "File name:", step.Get("file"));
                if (file == null) break;
                var content = PromptSnippetValue("Edit Step", "Content:", step.Get("content"));
                if (content == null) break;
                step.Params["file"] = file;
                step.Params["content"] = content;
                changed = true;
                break;
            }
            case "appendFile":
            {
                var file = PromptSnippetValue("Edit Step", "File name:", step.Get("file"));
                if (file == null) break;
                var line = PromptSnippetValue("Edit Step", "Line:", step.Get("line"));
                if (line == null) break;
                step.Params["file"] = file;
                step.Params["line"] = line;
                changed = true;
                break;
            }
            case "custom":
            {
                var v = PromptSnippetValue("Edit Step", "Script line:", step.Get("line"));
                if (v != null) { step.Params["line"] = v; changed = true; }
                break;
            }
            case "download":
                StatusText.Text = "Download step follows the form fields (URL, SHA1, SHA256, size).";
                return;
            default:
                MessageBox.Show(this, "This step has no editable parameters.", "Edit Step",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
        }

        if (changed)
        {
            UpdatePreview();
            StatusText.Text = "Step updated.";
        }
    }

    private void RawEditCheck_Changed(object sender, RoutedEventArgs e)
    {
        var raw = RawEditCheck.IsChecked == true;
        ActionScriptBox.IsReadOnly = !raw;
        if (!raw)
        {
            UpdatePreview();
        }
        else
        {
            StatusText.Text = "Raw edit mode: script will not re-render from steps until you exit.";
        }
    }

    private void ValidateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MainTabControl.SelectedItem is TabItem activeTab &&
            activeTab.Header is string hdr &&
            hdr.Equals("Analysis", StringComparison.OrdinalIgnoreCase))
        {
            AnalysisValidateBtn_Click(sender, e);
            return;
        }

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
        // If Analysis tab is active, save analysis content instead of fixlet
        if (MainTabControl.SelectedItem is TabItem activeTab &&
            activeTab.Header is string hdr &&
            hdr.Equals("Analysis", StringComparison.OrdinalIgnoreCase))
        {
            AnalysisSaveBtn_Click(sender, e);
            return;
        }

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

            var detection = RelevanceBuilder.BuildDetection(BuildDetectionInput(type));
            RelevanceBox.Text = string.Join(Environment.NewLine, detection.Relevance);

            if (string.IsNullOrWhiteSpace(SuccessBox.Text))
                SuccessBox.Text = detection.SuccessCriteria;

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

    /// <summary>
    /// Fills the form from an application installed on THIS machine (Uninstall registry keys):
    /// version, install path, uninstall string, registry key/value and MSI product code.
    /// </summary>
    private async void LocalDetectBtn_Click(object sender, RoutedEventArgs e)
    {
        var appName = AppNameBox.Text.Trim();
        if (appName.Length == 0)
        {
            MessageBox.Show(this, "Enter an app name first.", "Detect on this PC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LocalDetectBtn.IsEnabled = false;
        StatusText.Text = $"Searching this PC for '{appName}'...";
        try
        {
            var services = CliRunner.BuildServices();
            var scanner = services.GetRequiredService<IInstalledAppsScanner>();
            var apps = await scanner.ScanAsync(enrichWithWinget: false);

            var match = FindInstalledApp(apps, appName);
            if (match is null)
            {
                StatusText.Text = $"'{appName}' is not installed on this device.";
                MessageBox.Show(this,
                    $"'{appName}' was not found installed on this device.\n\n" +
                    "Check the spelling of the app name, or use Auto-Fetch (winget) to pull " +
                    "the metadata from the winget catalog instead.",
                    "App not installed", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AppNameBox.Text = match.Name;
            if (!string.IsNullOrWhiteSpace(match.Version))
                VersionBox.Text = match.Version;
            if (!string.IsNullOrWhiteSpace(match.UninstallString))
                UninstallStringBox.Text = match.UninstallString;
            if (!string.IsNullOrWhiteSpace(match.RegistryKeyPath))
                RegistryKeyBox.Text = match.RegistryKeyPath;
            if (!string.IsNullOrWhiteSpace(match.RegistryValueName))
                RegistryValueNameBox.Text = match.RegistryValueName;
            if (!string.IsNullOrWhiteSpace(match.Version))
                RegistryValueBox.Text = match.Version;
            if (!string.IsNullOrWhiteSpace(match.MsiProductCode))
                MsiCodeBox.Text = match.MsiProductCode;

            var localPath = LocalInstallPath(match);
            if (localPath.Length > 0)
                InstallPathBox.Text = localPath;

            var type = TypeCombo.SelectedItem as string ?? FixletTemplates.TypeInstall;
            var detection = RelevanceBuilder.BuildDetection(BuildDetectionInput(type));
            RelevanceBox.Text = string.Join(Environment.NewLine, detection.Relevance);
            SuccessBox.Text = detection.SuccessCriteria;
            UpdatePreview();

            StatusText.Text =
                $"Detected on this PC: {match.Name} {match.Version} - {DetectionMethods.Label(detection.Method)}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Local detection failed: {ex.Message}";
        }
        finally
        {
            LocalDetectBtn.IsEnabled = true;
        }
    }

    /// <summary>Best match for a user-typed app name among the installed applications.</summary>
    private static InstalledApp? FindInstalledApp(List<InstalledApp> apps, string query)
    {
        if (apps is null || apps.Count == 0) return null;
        var q = query.Trim();
        if (q.Length == 0) return null;

        var named = apps.Where(a => !string.IsNullOrWhiteSpace(a.Name)).ToList();
        if (named.Count == 0) return null;

        var exact = named.FirstOrDefault(a => string.Equals(a.Name.Trim(), q, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var contains = named.FirstOrDefault(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (contains is not null) return contains;

        var reverse = named.FirstOrDefault(a => a.Name.Length >= 3 &&
                                               q.Contains(a.Name, StringComparison.OrdinalIgnoreCase));
        if (reverse is not null) return reverse;

        // Word-level match: "chrome" -> "Google Chrome"
        return named.FirstOrDefault(a => a.Name
            .Split(new[] { ' ', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(w => w.Length >= 3 && q.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>InstallLocation directory → first executable inside it (otherwise the value as-is).</summary>
    private static string LocalInstallPath(InstalledApp app)
    {
        var location = (app.InstallLocation ?? "").Trim();
        if (location.Length == 0) return "";

        if (Directory.Exists(location))
        {
            var exe = Directory.GetFiles(location, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (exe is not null) return exe;
        }
        return location;
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
            if (mainTabs != null)
            {
                // Prefer Command Reference tab by header (tab order may change)
                for (int i = 0; i < mainTabs.Items.Count; i++)
                {
                    if (mainTabs.Items[i] is TabItem ti &&
                        ti.Header is string h && h.Contains("Command Reference", StringComparison.OrdinalIgnoreCase))
                    {
                        mainTabs.SelectedIndex = i;
                        break;
                    }
                }
            }
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
        else if (WindowState == WindowState.Maximized)
        {
            var wa = SystemParameters.WorkArea;
            s.WindowLeft = wa.Left;
            s.WindowTop = wa.Top;
            s.WindowWidth = wa.Width;
            s.WindowHeight = wa.Height;
        }
        s.Save();
    }

    // ─── Analysis (retrieved properties) ─────────────────────────────────

    private AnalysisModel BuildAnalysisModel()
    {
        var relevance = (AnalysisRelevanceBox.Text ?? "")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (relevance.Count == 0)
            relevance.Add("true");

        var props = _analysisProps
            .Select(p => new AnalysisProperty
            {
                Id = p.Id,
                Name = (p.Name ?? "").Trim(),
                Relevance = (p.Relevance ?? "").Trim(),
                EvaluationPeriod = AnalysisWriter.NormalizeEvaluationPeriod(p.EvaluationPeriod),
                KeepStatistics = p.KeepStatistics
            })
            .ToList();

        // Assign sequential IDs for display when unset
        for (int i = 0; i < props.Count; i++)
        {
            if (props[i].Id <= 0)
                props[i].Id = i + 1;
        }

        return new AnalysisModel
        {
            Title = AnalysisTitleBox.Text.Trim(),
            Category = string.IsNullOrWhiteSpace(AnalysisCategoryBox.Text) ? "BESPolicies" : AnalysisCategoryBox.Text.Trim(),
            Source = "FixletBuilder",
            SourceId = AnalysisSourceIdBox.Text.Trim(),
            Domain = "BESC",
            Description = AnalysisDescriptionBox.Text,
            Relevance = relevance,
            Properties = props
        };
    }

    private void UpdateAnalysisPreview()
    {
        if (_analysisBusy) return;
        _analysisBusy = true;
        try
        {
            if (AnalysisXmlPreviewBox is null || AnalysisPropsGrid is null)
                return;

            AnalysisPropCountText.Text = $"{_analysisProps.Count} propert{(_analysisProps.Count == 1 ? "y" : "ies")}";

            if (_analysisProps.Count == 0)
            {
                AnalysisXmlPreviewBox.Text =
                    "<!-- Add at least one property, or click Apply Template to load a starter analysis. -->";
                return;
            }

            var model = BuildAnalysisModel();
            var writer = new AnalysisWriter();
            try
            {
                AnalysisXmlPreviewBox.Text = writer.GenerateXml(model);
            }
            catch (Exception ex)
            {
                AnalysisXmlPreviewBox.Text = "<!-- " + ex.Message.Replace("-->", "--\\>") + " -->";
            }
        }
        finally
        {
            _analysisBusy = false;
        }
    }

    private void AnalysisField_Changed(object sender, TextChangedEventArgs e)
    {
        if (_analysisBusy) return;
        UpdateAnalysisPreview();
    }

    private void AnalysisApplyTemplateBtn_Click(object sender, RoutedEventArgs e)
    {
        var key = AnalysisTemplateCombo.SelectedItem as string ?? "app-status";
        var t = AnalysisTemplates.Get(
            key,
            AppNameBox.Text.Trim(),
            RegistryKeyBox.Text.Trim(),
            RegistryValueNameBox.Text.Trim(),
            RegistryValueBox.Text.Trim(),
            InstallPathBox.Text.Trim());

        _analysisBusy = true;
        try
        {
            AnalysisTitleBox.Text = t.Title;
            AnalysisCategoryBox.Text = t.Category;
            AnalysisDescriptionBox.Text = t.Description;
            AnalysisRelevanceBox.Text = string.Join(Environment.NewLine, t.Relevance);
            if (string.IsNullOrWhiteSpace(AnalysisSourceIdBox.Text))
                AnalysisSourceIdBox.Text = string.IsNullOrWhiteSpace(AppNameBox.Text.Trim())
                    ? t.Key
                    : AppNameBox.Text.Trim();

            _analysisProps.Clear();
            foreach (var p in t.Properties)
            {
                _analysisProps.Add(new AnalysisProperty
                {
                    Id = 0,
                    Name = p.Name,
                    Relevance = p.Relevance,
                    EvaluationPeriod = p.EvaluationPeriod,
                    KeepStatistics = p.KeepStatistics
                });
            }
        }
        finally
        {
            _analysisBusy = false;
        }

        AnalysisPropsGrid.ItemsSource = null;
        AnalysisPropsGrid.ItemsSource = _analysisProps;
        UpdateAnalysisPreview();
        StatusText.Text = $"Analysis template applied: {t.Key} ({_analysisProps.Count} properties).";
    }

    private void AnalysisAddPropertyBtn_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptSnippetValue("Add Analysis Property", "Property name:", "My Property");
        if (name == null) return;
        var relevance = PromptSnippetValue("Add Analysis Property",
            "Relevance expression (returns the property value):", "name of operating system");
        if (relevance == null) return;
        var period = PromptSnippetValue("Add Analysis Property",
            "Evaluation period (PT15M, PT1H, P1D, or blank = every report):", "PT1H");
        period ??= "";

        _analysisProps.Add(new AnalysisProperty
        {
            Id = 0,
            Name = name,
            Relevance = relevance,
            EvaluationPeriod = period
        });
        AnalysisPropsGrid.ItemsSource = null;
        AnalysisPropsGrid.ItemsSource = _analysisProps;
        AnalysisPropsGrid.SelectedIndex = _analysisProps.Count - 1;
        UpdateAnalysisPreview();
        StatusText.Text = $"Property added: {name}";
    }

    private void AnalysisRemovePropertyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (AnalysisPropsGrid.SelectedItem is not AnalysisProperty p)
        {
            StatusText.Text = "Select a property to remove.";
            return;
        }
        _analysisProps.Remove(p);
        AnalysisPropsGrid.ItemsSource = null;
        AnalysisPropsGrid.ItemsSource = _analysisProps;
        UpdateAnalysisPreview();
    }

    private void AnalysisMoveUpBtn_Click(object sender, RoutedEventArgs e)
    {
        if (AnalysisPropsGrid.SelectedItem is not AnalysisProperty p) return;
        var i = _analysisProps.IndexOf(p);
        if (i <= 0) return;
        _analysisProps.RemoveAt(i);
        _analysisProps.Insert(i - 1, p);
        AnalysisPropsGrid.ItemsSource = null;
        AnalysisPropsGrid.ItemsSource = _analysisProps;
        AnalysisPropsGrid.SelectedItem = p;
        UpdateAnalysisPreview();
    }

    private void AnalysisMoveDownBtn_Click(object sender, RoutedEventArgs e)
    {
        if (AnalysisPropsGrid.SelectedItem is not AnalysisProperty p) return;
        var i = _analysisProps.IndexOf(p);
        if (i < 0 || i >= _analysisProps.Count - 1) return;
        _analysisProps.RemoveAt(i);
        _analysisProps.Insert(i + 1, p);
        AnalysisPropsGrid.ItemsSource = null;
        AnalysisPropsGrid.ItemsSource = _analysisProps;
        AnalysisPropsGrid.SelectedItem = p;
        UpdateAnalysisPreview();
    }

    private void AnalysisPropsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // no-op; keeps grid interactive
    }

    private void AnalysisPropsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AnalysisPropsGrid.SelectedItem is not AnalysisProperty p) return;

        var name = PromptSnippetValue("Edit Property", "Property name:", p.Name);
        if (name == null) return;
        var relevance = PromptSnippetValue("Edit Property", "Relevance expression:", p.Relevance);
        if (relevance == null) return;
        var period = PromptSnippetValue("Edit Property",
            "Evaluation period (PT15M, PT1H, P1D, blank = every report):",
            string.IsNullOrWhiteSpace(p.EvaluationPeriod) ? "" : p.EvaluationPeriod);
        period ??= "";

        p.Name = name;
        p.Relevance = relevance;
        p.EvaluationPeriod = period;
        AnalysisPropsGrid.Items.Refresh();
        UpdateAnalysisPreview();
    }

    private void AnalysisSnippetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string snippet || snippet.Length == 0)
            return;

        if (AnalysisPropsGrid.SelectedItem is AnalysisProperty p)
        {
            p.Relevance = string.IsNullOrWhiteSpace(p.Relevance)
                ? snippet
                : p.Relevance + " " + snippet;
            AnalysisPropsGrid.Items.Refresh();
        }
        else
        {
            var pname = PromptSnippetValue("New Property from Snippet", "Property name:", "Snippet Property");
            if (pname == null) return;
            _analysisProps.Add(new AnalysisProperty
            {
                Id = 0,
                Name = pname,
                Relevance = snippet,
                EvaluationPeriod = "PT1H"
            });
            AnalysisPropsGrid.ItemsSource = null;
            AnalysisPropsGrid.ItemsSource = _analysisProps;
        }
        UpdateAnalysisPreview();
    }

    private void AnalysisValidateBtn_Click(object sender, RoutedEventArgs e)
    {
        var model = BuildAnalysisModel();
        var writer = new AnalysisWriter();
        var issues = writer.Validate(model);
        if (issues.Count == 0)
        {
            StatusText.Text = "Analysis is valid.";
            MessageBox.Show(this,
                $"Analysis is valid.\n\n{model.Properties.Count} propert(y/ies), targeting:\n  {string.Join("\n  ", model.Relevance)}",
                "Validation", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            StatusText.Text = $"{issues.Count} issue(s) found.";
            MessageBox.Show(this, string.Join(Environment.NewLine, issues),
                "Validation issues", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AnalysisSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var model = BuildAnalysisModel();
        var writer = new AnalysisWriter();
        var issues = writer.Validate(model);
        if (issues.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, issues),
                "Validation issues", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = AppSettings.Current;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Analysis",
            FileName = FixletFactory.SanitizeFileName(model.Title) + ".bes",
            Filter = "BigFix Analysis (*.bes)|*.bes|All files (*.*)|*.*",
            DefaultExt = ".bes"
        };
        if (!string.IsNullOrWhiteSpace(settings.LastSaveFolder) && Directory.Exists(settings.LastSaveFolder))
            dialog.InitialDirectory = settings.LastSaveFolder;

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            writer.WriteBes(dialog.FileName, model);
            settings.LastSaveFolder = Path.GetDirectoryName(dialog.FileName) ?? "";
            settings.Save();
            StatusText.Text = $"Analysis saved: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save analysis:\n" + ex.Message,
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AnalysisPublishBtn_Click(object sender, RoutedEventArgs e)
    {
        var model = BuildAnalysisModel();
        var writer = new AnalysisWriter();
        var issues = writer.Validate(model);
        if (issues.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, issues),
                "Validation issues", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string xml;
        try
        {
            xml = writer.GenerateXml(model);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "XML generation failed:\n" + ex.Message,
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (AnalysisWhatIfCheck.IsChecked == true)
        {
            AnalysisXmlPreviewBox.Text = xml;
            StatusText.Text = "What-if: analysis XML printed to preview (not published).";
            MessageBox.Show(this, "What-if mode:\n\n" + xml, "Analysis XML", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var services = CliRunner.BuildServices();
        var client = services.GetService<IBigFixConsoleClient>();
        if (client is null)
        {
            MessageBox.Show(this,
                "BigFix console is not configured.\nAdd BaseUrl/Username/Password under BigFix in appsettings.json.",
                "Publish", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var site = AnalysisSiteBox.Text.Trim();
        AnalysisPublishBtn.IsEnabled = false;
        StatusText.Text = "Publishing analysis to BigFix...";

        try
        {
            var result = await client.ImportAnalysisAsync(xml, string.IsNullOrWhiteSpace(site) ? null : site);
            if (result.Success)
            {
                StatusText.Text = "Analysis published" +
                    (result.FixletId.HasValue ? $" (ID {result.FixletId})" : "") + ".";
                MessageBox.Show(this,
                    "Analysis published successfully." +
                    (result.FixletId.HasValue ? $"\nID: {result.FixletId}" : ""),
                    "Publish", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText.Text = "Analysis publish failed.";
                MessageBox.Show(this, "Publish failed:\n" + result.Message,
                    "Publish", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Analysis publish failed.";
            MessageBox.Show(this, "Publish failed:\n" + ex.Message,
                "Publish", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            AnalysisPublishBtn.IsEnabled = true;
        }
    }
}
