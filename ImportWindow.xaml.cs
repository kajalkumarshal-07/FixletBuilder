using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using ClosedXML.Excel;
using FixletBuilder.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace FixletBuilder;

public class MonitorResult
{
    public bool UpdateAvailable { get; set; }
    public string AppName { get; set; } = "";
    public string InstalledVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public string PackageId { get; set; } = "";
    public string Source { get; set; } = "";
}

public partial class ImportWindow : Window
{
    private string[] _headers = Array.Empty<string>();
    private List<string[]> _dataRows = new();
    private readonly OpenFileDialog _fileDialog;
    private readonly AppSettings _settings = AppSettings.Current;
    private string? _loadedFile;
    private string? _generatedDir;
    private readonly ObservableCollection<MonitorResult> _monitorResults = new();
    private readonly IServiceProvider _services;
    private readonly IAppVersionMonitor _monitor;
    private readonly IWingetIntegration _winget;
    private readonly IFixletWriter _writer;

    public ImportWindow()
    {
        InitializeComponent();

        _services = CliRunner.BuildServices();
        _monitor = _services.GetRequiredService<IAppVersionMonitor>();
        _winget = _services.GetRequiredService<IWingetIntegration>();
        _writer = new FixletWriter(NullLogger<FixletWriter>.Instance);

        var items = new List<string> { "From file (Type column)" };
        items.AddRange(FixletTemplates.Types);
        TypeCombo.ItemsSource = items;
        TypeCombo.SelectedIndex = 0;

        MonitorGrid.ItemsSource = _monitorResults;

        if (!string.IsNullOrWhiteSpace(_settings.LastOutputFolder))
            OutputFolderBox.Text = _settings.LastOutputFolder;

        _fileDialog = new OpenFileDialog
        {
            Title = "Select CSV or Excel file",
            Filter = "CSV or Excel files (*.csv;*.xlsx;*.xls)|*.csv;*.xlsx;*.xls|All files (*.*)|*.*"
        };
    }

    private void LoadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_fileDialog.ShowDialog(this) != true)
            return;
        LoadFile(_fileDialog.FileName);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            LoadFile(files[0]);
    }

    private void LoadFile(string path)
    {
        try
        {
            List<string[]> rows = LoadRows(path);
            if (rows.Count < 2)
            {
                MessageBox.Show(this, "The file does not contain a header row with data.", "No data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _headers = rows[0];
            _dataRows = rows.Skip(1).ToList();

            var table = new DataTable();
            foreach (var h in _headers)
                table.Columns.Add(string.IsNullOrWhiteSpace(h) ? "Column" + table.Columns.Count : h);
            foreach (var row in _dataRows.Take(1000))
            {
                var r = table.NewRow();
                for (int i = 0; i < row.Length; i++)
                    r[i] = row[i];
                table.Rows.Add(r);
            }

            PreviewGrid.ItemsSource = table.DefaultView;
            FileLabel.Text = $"{Path.GetFileName(path)} - {_dataRows.Count} data rows";
            GenerateBtn.IsEnabled = true;
            StatusText.Text = "";
            IssuesList.Items.Clear();

            _loadedFile = path;
            _settings.LastImportFile = path;
            _settings.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not load file:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static List<string[]> LoadRows(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".csv")
            return CsvReader.Parse(File.ReadAllText(path, Encoding.UTF8));

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        var range = ws.RangeUsed();
        if (range is null)
            return new List<string[]>();

        int rows = range.RowCount();
        int cols = range.ColumnCount();
        var result = new List<string[]>(rows);
        for (int r = 1; r <= rows; r++)
        {
            var row = new string[cols];
            for (int c = 1; c <= cols; c++)
                row[c - 1] = range.Cell(r, c).GetString();
            result.Add(row);
        }
        return result;
    }

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose output folder" };
        if (!string.IsNullOrWhiteSpace(OutputFolderBox.Text) && Directory.Exists(OutputFolderBox.Text))
            dialog.InitialDirectory = OutputFolderBox.Text;
        if (dialog.ShowDialog(this) == true)
            OutputFolderBox.Text = dialog.FolderName;
    }

    private void GenerateBtn_Click(object sender, RoutedEventArgs e)
    {
        var outputDir = OutputFolderBox.Text.Trim();
        if (outputDir.Length == 0)
        {
            MessageBox.Show(this, "Choose an output folder first.", "Output folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try { Directory.CreateDirectory(outputDir); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not create output folder:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string? forcedType = TypeCombo.SelectedIndex <= 0 ? null : TypeCombo.SelectedItem as string;
        var map = FixletFactory.BuildColumnMap(_headers);

        int ok = 0;
        int skipped = 0;
        var problems = new List<string>();
        var issuesList = new List<string>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var report = new List<string>
        {
            "FixletBuilder generation report",
            $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Input file: {_loadedFile ?? "(unknown)"}",
            $"Fixlet type: {(forcedType ?? "from file (Type column)")}",
            $"Output folder: {outputDir}",
            new string('-', 60)
        };

        for (int i = 0; i < _dataRows.Count; i++)
        {
            var row = _dataRows[i];
            int rowNumber = i + 2;

            FixletModel model;
            try
            {
                model = FixletFactory.Build(row, map, forcedType, out var issues);
                foreach (var issue in issues)
                {
                    problems.Add(issue);
                    report.Add($"[WARN] Row {rowNumber}: {issue}");
                    issuesList.Add($"Row {rowNumber}: {issue}");
                }

                if (string.IsNullOrWhiteSpace(model.Title))
                {
                    skipped++;
                    report.Add($"[SKIP] Row {rowNumber}: no title");
                    issuesList.Add($"Row {rowNumber}: SKIPPED - no title");
                    continue;
                }
            }
            catch (Exception ex)
            {
                problems.Add("Row failed: " + ex.Message);
                skipped++;
                report.Add($"[SKIP] Row {rowNumber}: {ex.Message}");
                issuesList.Add($"Row {rowNumber}: SKIPPED - {ex.Message}");
                continue;
            }

            var baseName = FixletFactory.SanitizeFileName(model.Title);
            var fileName = baseName + ".bes";
            int n = 2;
            while (usedNames.Contains(fileName))
                fileName = $"{baseName} ({n++}).bes";
            usedNames.Add(fileName);

            try
            {
                _writer.WriteBes(Path.Combine(outputDir, fileName), model);
                ok++;
                report.Add($"[OK]   {fileName} - {model.Title}");
            }
            catch (Exception ex)
            {
                skipped++;
                problems.Add($"'{model.Title}': {ex.Message}");
                report.Add($"[FAIL] Row {rowNumber}: {ex.Message}");
            }
        }

        report.Add(new string('-', 60));
        report.Add($"Summary: {ok} created, {skipped} skipped, {problems.Count} warning(s).");

        try
        {
            File.WriteAllText(Path.Combine(outputDir, "generation-report.txt"), string.Join(Environment.NewLine, report), new UTF8Encoding(false));
        }
        catch
        {
        }

        IssuesList.Items.Clear();
        foreach (var line in issuesList)
            IssuesList.Items.Add(line);

        _generatedDir = outputDir;
        OpenFolderBtn.IsEnabled = true;
        _settings.LastOutputFolder = outputDir;
        _settings.Save();

        string summary = $"{ok} fixlet(s) created in {outputDir}.";
        if (problems.Count > 0)
        {
            var sample = string.Join(Environment.NewLine, problems.Take(30));
            summary += $"\n\n{problems.Count} warning(s)/error(s):\n{sample}";
            if (problems.Count > 30)
                summary += $"\n... and {problems.Count - 30} more.";
            MessageBox.Show(this, summary, "Generation finished", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(this, summary, "Generation finished", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        StatusText.Text = $"{ok} created, {skipped} skipped, {problems.Count} warnings. Report: generation-report.txt";
    }

    private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_generatedDir is not null && Directory.Exists(_generatedDir))
            Process.Start("explorer.exe", $"\"{Path.GetFullPath(_generatedDir)}\"");
    }

    private async void MonitorCheckBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_winget.IsAvailable())
        {
            MessageBox.Show(this, "winget is not installed or not available.", "Winget", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MonitorCheckBtn.IsEnabled = false;
        MonitorStatus.Text = "Checking for updates...";

        try
        {
            var results = await _monitor.CheckAllAsync();
            _monitorResults.Clear();

            foreach (var r in results)
            {
                _monitorResults.Add(new MonitorResult
                {
                    UpdateAvailable = r.UpdateAvailable,
                    AppName = r.AppName,
                    InstalledVersion = r.InstalledVersion,
                    LatestVersion = r.LatestVersion,
                    PackageId = r.PackageId,
                    Source = r.Source
                });
            }

            var updateCount = results.Count(r => r.UpdateAvailable);
            MonitorStatus.Text = $"{results.Count} app(s) checked, {updateCount} update(s) available.";
            MonitorGenerateBtn.IsEnabled = updateCount > 0;
            MonitorExportCsvBtn.IsEnabled = results.Count > 0;
        }
        catch (Exception ex)
        {
            MonitorStatus.Text = $"Check failed: {ex.Message}";
        }
        finally
        {
            MonitorCheckBtn.IsEnabled = true;
        }
    }

    private void MonitorGenerateBtn_Click(object sender, RoutedEventArgs e)
    {
        var outputDir = OutputFolderBox.Text.Trim();
        if (outputDir.Length == 0)
        {
            MessageBox.Show(this, "Choose an output folder first.", "Output folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try { Directory.CreateDirectory(outputDir); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Cannot create output folder:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var updateResults = _monitorResults
            .Where(r => r.UpdateAvailable)
            .Select(r => new VersionCheckResult
            {
                AppName = r.AppName,
                InstalledVersion = r.InstalledVersion,
                LatestVersion = r.LatestVersion,
                PackageId = r.PackageId,
                UpdateAvailable = r.UpdateAvailable,
                Source = r.Source
            })
            .ToList();

        var fixlets = _monitor.GenerateUpgradeFixlets(updateResults);

        int ok = 0;
        foreach (var fixlet in fixlets)
        {
            try
            {
                var fileName = FixletFactory.SanitizeFileName(fixlet.Title) + ".bes";
                _writer.WriteBes(Path.Combine(outputDir, fileName), fixlet);
                ok++;
            }
            catch
            {
                // skip
            }
        }

        _settings.LastOutputFolder = outputDir;
        _settings.Save();

        MessageBox.Show(this, $"{ok} upgrade fixlet(s) generated in:\n{outputDir}", "Done",
            MessageBoxButton.OK, MessageBoxImage.Information);
        StatusText.Text = $"{ok} upgrade fixlet(s) generated.";
    }

    private void MonitorExportCsvBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Version Check CSV",
            FileName = "version-check.csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var results = _monitorResults.Select(r => new VersionCheckResult
            {
                AppName = r.AppName,
                InstalledVersion = r.InstalledVersion,
                LatestVersion = r.LatestVersion,
                PackageId = r.PackageId,
                UpdateAvailable = r.UpdateAvailable,
                Source = r.Source
            }).ToList();

            File.WriteAllText(dialog.FileName, _monitor.ExportResultsCsv(results), new UTF8Encoding(false));
            StatusText.Text = $"CSV exported to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}