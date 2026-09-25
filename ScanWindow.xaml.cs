using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FixletBuilder.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FixletBuilder;

public class SelectableApp : INotifyPropertyChanged
{
    public InstalledApp App { get; set; } = new();

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    public string Name => App.Name;
    public string Version => App.Version;
    public string Publisher => App.Publisher;
    public string InstallLocation => App.InstallLocation;
    public string WingetId => App.WingetId;
    public string Source => App.Source;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class UpdateInfo
{
    public string AppName { get; set; } = "";
    public string InstalledVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public string PackageId { get; set; } = "";
    public string Source { get; set; } = "";
}

public partial class ScanWindow : Window
{
    private readonly ObservableCollection<SelectableApp> _apps = new();
    private readonly ObservableCollection<UpdateInfo> _updates = new();
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly IServiceProvider _services;
    private readonly IInstalledAppsScanner _scanner;
    private readonly IWingetIntegration _winget;
    private readonly IFixletWriter _writer;

    public ScanWindow()
    {
        InitializeComponent();

        _services = CliRunner.BuildServices();
        _scanner = _services.GetRequiredService<IInstalledAppsScanner>();
        _winget = _services.GetRequiredService<IWingetIntegration>();
        _writer = new FixletWriter(NullLogger<FixletWriter>.Instance);

        TypeCombo.ItemsSource = FixletTemplates.Types;
        TypeCombo.SelectedIndex = 0;

        AppsGrid.ItemsSource = _apps;
        UpdatesGrid.ItemsSource = _updates;

        if (!string.IsNullOrWhiteSpace(_settings.LastOutputFolder))
            OutputBox.Text = _settings.LastOutputFolder;

        WingetStatus.Text = _winget.IsAvailable() ? "winget: available" : "winget: not found";
    }

    private async void ScanBtn_Click(object sender, RoutedEventArgs e)
    {
        ScanBtn.IsEnabled = false;
        ScanStatus.Text = "Scanning installed applications...";

        try
        {
            var apps = await _scanner.ScanAsync();
            _apps.Clear();
            foreach (var app in apps)
                _apps.Add(new SelectableApp { App = app });

            ScanStatus.Text = $"Found {_apps.Count} applications.";
            UpdateCounts();
            GenerateBtn.IsEnabled = _apps.Count > 0;
            GenerateAllBtn.IsEnabled = _apps.Count > 0;
        }
        catch (Exception ex)
        {
            ScanStatus.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            ScanBtn.IsEnabled = true;
        }
    }

    private async void FetchWingetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_winget.IsAvailable())
        {
            MessageBox.Show(this, "winget is not installed or not available.", "Winget", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var query = FilterBox.Text.Trim();
        if (query.Length == 0)
        {
            MessageBox.Show(this, "Enter a search term in the filter box first.", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        FetchWingetBtn.IsEnabled = false;
        ScanStatus.Text = $"Searching winget for '{query}'...";

        try
        {
            var results = await _winget.SearchAsync(query);
            _apps.Clear();
            foreach (var pkg in results)
            {
                _apps.Add(new SelectableApp
                {
                    App = new InstalledApp
                    {
                        Name = pkg.Name,
                        Version = pkg.Version,
                        WingetId = pkg.Id,
                        Source = "winget"
                    }
                });
            }

            ScanStatus.Text = $"Found {_apps.Count} package(s) from winget.";
            UpdateCounts();
            GenerateBtn.IsEnabled = _apps.Count > 0;
            GenerateAllBtn.IsEnabled = _apps.Count > 0;
        }
        catch (Exception ex)
        {
            ScanStatus.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            FetchWingetBtn.IsEnabled = true;
        }
    }

    private async void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_winget.IsAvailable())
        {
            MessageBox.Show(this, "winget is not installed or not available.", "Winget", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CheckUpdatesBtn.IsEnabled = false;
        ScanStatus.Text = "Checking for available updates...";

        try
        {
            var upgrades = await _winget.ListUpgradesAsync();
            _updates.Clear();
            foreach (var pkg in upgrades)
            {
                _updates.Add(new UpdateInfo
                {
                    AppName = pkg.Name,
                    InstalledVersion = pkg.Version,
                    LatestVersion = pkg.AvailableVersion,
                    PackageId = pkg.Id,
                    Source = pkg.Source
                });
            }

            ScanStatus.Text = $"{_updates.Count} update(s) available.";
            LogList.Items.Add($"[{DateTime.Now:HH:mm:ss}] Found {_updates.Count} update(s).");
        }
        catch (Exception ex)
        {
            ScanStatus.Text = $"Update check failed: {ex.Message}";
        }
        finally
        {
            CheckUpdatesBtn.IsEnabled = true;
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var check = SelectAllCheck.IsChecked == true;
        foreach (var app in _apps)
            app.IsSelected = check;
        UpdateCounts();
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var view = CollectionViewSource.GetDefaultView(_apps);
        var filter = FilterBox.Text.Trim();
        if (filter.Length == 0)
        {
            view.Filter = null;
        }
        else
        {
            view.Filter = o =>
            {
                if (o is SelectableApp app)
                    return app.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                           app.Publisher.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                           app.WingetId.Contains(filter, StringComparison.OrdinalIgnoreCase);
                return false;
            };
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose output folder" };
        if (!string.IsNullOrWhiteSpace(OutputBox.Text) && Directory.Exists(OutputBox.Text))
            dialog.InitialDirectory = OutputBox.Text;
        if (dialog.ShowDialog(this) == true)
            OutputBox.Text = dialog.FolderName;
    }

    private void GenerateBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _apps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select at least one application.", "No selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        GenerateFixlets(selected.Select(a => a.App).ToList());
    }

    private void GenerateAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_apps.Count == 0) return;
        GenerateFixlets(_apps.Select(a => a.App).ToList());
    }

    private void MonitorBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _apps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select at least one application to monitor.", "No selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var monitor = _services.GetRequiredService<IAppVersionMonitor>();
        foreach (var app in selected)
            monitor.TrackApp(app.Name);

        LogList.Items.Add($"[{DateTime.Now:HH:mm:ss}] Started monitoring {selected.Count} app(s).");
        ScanStatus.Text = $"{selected.Count} app(s) added to monitor.";
    }

    private void GenerateFixlets(List<InstalledApp> apps)
    {
        var output = OutputBox.Text.Trim();
        if (output.Length == 0)
        {
            MessageBox.Show(this, "Enter an output folder path.", "Output", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try { Directory.CreateDirectory(output); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Cannot create output folder:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var type = TypeCombo.SelectedItem as string ?? FixletTemplates.TypeInstall;
        var processName = ProcessNameBox.Text.Trim();
        var serviceName = ServiceNameBox.Text.Trim();
        int ok = 0, skipped = 0;

        LogList.Items.Clear();

        foreach (var app in apps)
        {
            try
            {
                var template = FixletTemplates.Apply(type, app.Name, app.Version,
                    "", SilentArgsDatabase.GetForApp(app.Name), app.InstallLocation, app.UninstallString);

                var relevance = RelevanceBuilder.BuildFromInstalledApp(app, type);

                var registryKeyPath = app.RegistryKeyPath;
                var registryValueName = app.RegistryValueName;
                if (string.IsNullOrWhiteSpace(registryValueName)) registryValueName = "DisplayVersion";
                var registryValue = app.RegistryValue;

                var model = new FixletModel
                {
                    Title = template.Title,
                    Category = "Applications",
                    Source = "FixletBuilder Scan",
                    SourceId = app.WingetId,
                    Relevance = relevance,
                    Description = template.Description,
                    ActionDescription = template.ActionDescription,
                    ActionScript = template.ActionScript,
                    SuccessCriteria = RelevanceBuilder.BuildSuccessCriteriaFromInstalledApp(app, type),
                    RegistryKeyPath = registryKeyPath,
                    RegistryValueName = registryValueName,
                    RegistryValue = registryValue
                };

                var fileName = FixletFactory.SanitizeFileName(model.Title) + ".bes";
                _writer.WriteBes(Path.Combine(output, fileName), model);

                ok++;
                LogList.Items.Add($"[{DateTime.Now:HH:mm:ss}] [OK] {model.Title}");
            }
            catch (Exception ex)
            {
                skipped++;
                LogList.Items.Add($"[{DateTime.Now:HH:mm:ss}] [SKIP] {app.Name}: {ex.Message}");
            }
        }

        _settings.LastOutputFolder = output;
        _settings.Save();

        StatusText.Text = $"{ok} created, {skipped} skipped.";
        LogList.Items.Add($"[{DateTime.Now:HH:mm:ss}] Done. {ok} fixlet(s) in {Path.GetFullPath(output)}");

        if (ok > 0)
        {
            var result = MessageBox.Show(this,
                $"{ok} fixlet(s) created in:\n{Path.GetFullPath(output)}\n\nOpen folder?",
                "Generation complete", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
                Process.Start("explorer.exe", $"\"{Path.GetFullPath(output)}\"");
        }
    }

    private void UpdateCounts()
    {
        var selected = _apps.Count(a => a.IsSelected);
        CountsText.Text = $"{selected} selected / {_apps.Count} total";
    }
}