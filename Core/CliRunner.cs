using System.CommandLine;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace FixletBuilder.Core;

public static class CliRunner
{
    public static async Task<int> Run(string[] args, IServiceProvider? services = null)
    {
        services ??= BuildServices();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("CliRunner");

        var rootCommand = new RootCommand("FixletBuilder - BigFix Fixlet Generator (CLI Mode)");

        // scan
        var scanCommand = new Command("scan", "Scan installed applications and generate fixlets");
        var scanOutput = new Option<string>(new[] { "-o", "--output" }, () => "./fixlet-output", "Output directory");
        var scanType = new Option<string>(new[] { "-t", "--type" }, () => "install", "Fixlet type: install, upgrade, uninstall, custom");
        var scanFilter = new Option<string[]>(new[] { "-f", "--filter" }, "Filter apps by name (partial match)");
        var scanExclude = new Option<string[]>(new[] { "-e", "--exclude" }, "Exclude apps by name (partial match)");
        var scanFormat = new Option<string>(new[] { "--format" }, () => "bes", "Output format: bes, csv, json");
        var scanImport = new Option<bool>(new[] { "--import" }, "Import generated fixlets into BigFix console");
        var scanSite = new Option<string?>(new[] { "--site" }, "Target BigFix site for import");
        scanCommand.AddOption(scanOutput); scanCommand.AddOption(scanType);
        scanCommand.AddOption(scanFilter); scanCommand.AddOption(scanExclude);
        scanCommand.AddOption(scanFormat); scanCommand.AddOption(scanImport); scanCommand.AddOption(scanSite);
        scanCommand.SetHandler((o, t, f, e, fmt, imp, site) =>
            RunScanAsync(services, o, t, f, e, fmt, imp, site),
            scanOutput, scanType, scanFilter, scanExclude, scanFormat, scanImport, scanSite);

        // monitor
        var monitorCommand = new Command("monitor", "Check for application updates and generate upgrade fixlets");
        var monitorOutput = new Option<string>(new[] { "-o", "--output" }, () => "./fixlet-output", "Output directory");
        var monitorApps = new Option<string[]>(new[] { "-a", "--apps" }, "Specific apps to monitor");
        var monitorAdd = new Option<string[]>(new[] { "--track" }, "Add apps to tracking list");
        var monitorRemove = new Option<string[]>(new[] { "--untrack" }, "Remove apps from tracking list");
        var monitorCsv = new Option<bool>(new[] { "--csv" }, "Export results as CSV");
        var monitorImport = new Option<bool>(new[] { "--import" }, "Import generated fixlets into BigFix console");
        var monitorSite = new Option<string?>(new[] { "--site" }, "Target BigFix site for import");
        monitorCommand.AddOption(monitorOutput); monitorCommand.AddOption(monitorApps);
        monitorCommand.AddOption(monitorAdd); monitorCommand.AddOption(monitorRemove);
        monitorCommand.AddOption(monitorCsv); monitorCommand.AddOption(monitorImport);
        monitorCommand.AddOption(monitorSite);
monitorCommand.SetHandler((o, a, add, rem, csv, imp, site) =>
            RunMonitorAsync(services, o, a, add, rem, csv, imp, site),
            monitorOutput, monitorApps, monitorAdd, monitorRemove, monitorCsv, monitorImport,
            monitorSite);
        // fetch
        var fetchCommand = new Command("fetch", "Fetch app metadata from winget");
        var fetchQuery = new Option<string?>(new[] { "-q", "--query" }, "Search query");
        var fetchId = new Option<string?>(new[] { "-i", "--id" }, "Package ID for details");
        fetchCommand.AddOption(fetchQuery); fetchCommand.AddOption(fetchId);
        fetchCommand.SetHandler((q, i) => RunFetch(services, q, i), fetchQuery, fetchId);

        // batch
        var batchCommand = new Command("batch", "Batch generate fixlets from CSV/Excel");
        var batchInput = new Option<string>(new[] { "-i", "--input" }, "Input CSV or Excel file path");
        var batchOutput = new Option<string>(new[] { "-o", "--output" }, () => "./fixlet-output", "Output directory");
        var batchType = new Option<string?>(new[] { "-t", "--type" }, "Force fixlet type for all rows");
        var batchImport = new Option<bool>(new[] { "--import" }, "Import generated fixlets into BigFix console");
        var batchSite = new Option<string?>(new[] { "--site" }, "Target BigFix site for import");
        batchCommand.AddOption(batchInput); batchCommand.AddOption(batchOutput);
        batchCommand.AddOption(batchType); batchCommand.AddOption(batchImport);
        batchCommand.AddOption(batchSite);
        batchCommand.SetHandler((i, o, t, imp, site) =>
            RunBatchAsync(services, i, o, t, imp, site),
            batchInput, batchOutput, batchType, batchImport, batchSite);

        // create
        var createCommand = new Command("create", "Create a single fixlet from CLI args");
        var createName = new Option<string>(new[] { "-n", "--name" }, "Application name");
        var createVersion = new Option<string?>(new[] { "-v", "--version" }, "Version");
        var createType = new Option<string>(new[] { "-t", "--type" }, () => "install", "Fixlet type");
        var createUrl = new Option<string?>(new[] { "-u", "--url" }, "Installer URL");
        var createPath = new Option<string?>(new[] { "-p", "--path" }, "Install path");
        var createOutput = new Option<string>(new[] { "-o", "--output" }, () => "./fixlet-output", "Output directory");
        var createSilent = new Option<string?>(new[] { "-s", "--silent-args" }, "Silent install arguments");
        var createAuto = new Option<bool>(new[] { "--auto" }, "Auto-fill metadata from winget");
        var createImport = new Option<bool>(new[] { "--import" }, "Import generated fixlet into BigFix console");
        var createSite = new Option<string?>(new[] { "--site" }, "Target BigFix site for import");
        createCommand.AddOption(createName); createCommand.AddOption(createVersion);
        createCommand.AddOption(createType); createCommand.AddOption(createUrl);
        createCommand.AddOption(createPath); createCommand.AddOption(createOutput);
        createCommand.AddOption(createSilent); createCommand.AddOption(createAuto);
        createCommand.AddOption(createImport); createCommand.AddOption(createSite);
        createCommand.SetHandler(ctx =>
        {
            var n = ctx.ParseResult.GetValueForOption(createName);
            var v = ctx.ParseResult.GetValueForOption(createVersion);
            var t = ctx.ParseResult.GetValueForOption(createType) ?? "install";
            var u = ctx.ParseResult.GetValueForOption(createUrl);
            var p = ctx.ParseResult.GetValueForOption(createPath);
            var o = ctx.ParseResult.GetValueForOption(createOutput) ?? "./fixlet-output";
            var s = ctx.ParseResult.GetValueForOption(createSilent);
            var auto = ctx.ParseResult.GetValueForOption(createAuto);
            var imp = ctx.ParseResult.GetValueForOption(createImport);
            var site = ctx.ParseResult.GetValueForOption(createSite);
            RunCreateAsync(services, n, v, t, u, p, o, s, auto, imp, site);
        });

        // console-ping
        var pingCommand = new Command("ping", "Check BigFix console connectivity");
        pingCommand.SetHandler(() => RunPing(services));

        rootCommand.AddCommand(scanCommand);
        rootCommand.AddCommand(monitorCommand);
        rootCommand.AddCommand(fetchCommand);
        rootCommand.AddCommand(batchCommand);
        rootCommand.AddCommand(createCommand);
        rootCommand.AddCommand(pingCommand);

        try
        {
            return await rootCommand.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unhandled CLI exception");
            return 99;
        }
    }

    public static IServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.AddLogging(b => b.AddSerilog());
        sc.AddHttpClient<IDownloadService, DownloadService>();
        sc.AddSingleton<IInstalledAppsScanner, InstalledAppsScanner>();
        sc.AddSingleton<IWingetIntegration, WingetIntegration>();
        sc.AddSingleton<IAppVersionMonitor, AppVersionMonitor>();
        sc.AddSingleton<IFixletWriter, FixletWriter>();
        return sc.BuildServiceProvider();
    }

    private static void RunScanAsync(IServiceProvider sp, string output, string type,
        string[]? filter, string[]? exclude, string format, bool importToConsole, string? site) =>
        RunScan(sp, output, type, filter, exclude, format, importToConsole, site).GetAwaiter().GetResult();

    private static void RunMonitorAsync(IServiceProvider sp, string output, string[]? apps,
        string[]? track, string[]? untrack, bool csv, bool importToConsole, string? site) =>
        RunMonitor(sp, output, apps, track, untrack, csv, importToConsole, site).GetAwaiter().GetResult();

    private static void RunFetchAsync(IServiceProvider sp, string? query, string? id) =>
        RunFetch(sp, query, id).GetAwaiter().GetResult();

    private static void RunBatchAsync(IServiceProvider sp, string input, string output,
        string? type, bool importToConsole, string? site) =>
        RunBatch(sp, input, output, type, importToConsole, site).GetAwaiter().GetResult();

    private static void RunCreateAsync(IServiceProvider sp, string? name, string? version,
        string type, string? url, string? path, string output, string? silentArgs, bool auto,
        bool importToConsole, string? site) =>
        RunCreate(sp, name, version, type, url, path, output, silentArgs, auto, importToConsole, site)
            .GetAwaiter().GetResult();

    private static async Task RunScan(IServiceProvider sp, string output, string type,
        string[]? filter, string[]? exclude, string format, bool importToConsole, string? site)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("scan");
        var scanner = sp.GetRequiredService<IInstalledAppsScanner>();
        var writer = sp.GetRequiredService<IFixletWriter>();

        Console.WriteLine("Scanning installed applications...");
        var apps = await scanner.ScanAsync();
        Console.WriteLine($"Found {apps.Count} installed applications.");

        if (filter is { Length: > 0 })
        {
            apps = apps.Where(a => filter.Any(f => a.Name.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
            Console.WriteLine($"Filtered to {apps.Count} applications.");
        }
        if (exclude is { Length: > 0 })
        {
            apps = apps.Where(a => !exclude.Any(e => a.Name.Contains(e, StringComparison.OrdinalIgnoreCase))).ToList();
            Console.WriteLine($"After exclusions: {apps.Count} applications.");
        }

        Directory.CreateDirectory(output);
        var generated = new List<string>();
        int created = 0;

        foreach (var app in apps)
        {
            try
            {
                var model = BuildModelFromApp(app, type);
                var fileName = FixletFactory.SanitizeFileName(model.Title) + ".bes";
                var filePath = Path.Combine(output, fileName);

                if (format == "json")
                {
                    var jsonPath = Path.ChangeExtension(filePath, ".json");
                    writer.WriteJson(jsonPath, model);
                    generated.Add(jsonPath);
                }
                else if (format == "csv")
                {
                    var csvPath = Path.Combine(output, "apps.csv");
                    File.AppendAllText(csvPath,
                        $"\"{model.Title}\",\"{app.Name}\",\"{app.Version}\",\"{app.InstallLocation}\",\"{type}\"\n");
                }
                else
                {
                    writer.WriteBes(filePath, model);
                    generated.Add(filePath);
                }

                created++;
                Console.WriteLine($"  [{created}] {model.Title}");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipped {App}", app.Name);
                Console.WriteLine($"  [SKIP] {app.Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"\nDone. {created} fixlet(s) created in {Path.GetFullPath(output)}");

        if (importToConsole)
            await ImportAllAsync(sp, generated, site);
    }

    private static async Task RunMonitor(IServiceProvider sp, string output, string[]? apps,
        string[]? track, string[]? untrack, bool csv, bool importToConsole, string? site)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("monitor");
        var monitor = sp.GetRequiredService<IAppVersionMonitor>();
        var writer = sp.GetRequiredService<IFixletWriter>();

        if (track is { Length: > 0 })
            foreach (var app in track) monitor.TrackApp(app);

        if (untrack is { Length: > 0 })
            foreach (var app in untrack) monitor.UntrackApp(app);

        if (apps is { Length: > 0 })
            foreach (var app in apps) monitor.TrackApp(app);

        if (monitor.Config.TrackedApps.Count == 0)
        {
            Console.WriteLine("No apps to monitor. Use --track to add apps or --apps to specify.");
            return;
        }

        Console.WriteLine($"Checking {monitor.Config.TrackedApps.Count} tracked app(s) for updates...");
        var results = await monitor.CheckAllAsync();
        var updates = results.Where(r => r.UpdateAvailable).ToList();

        Console.WriteLine($"\nResults: {results.Count} checked, {updates.Count} update(s) available.\n");
        foreach (var r in results)
        {
            var status = r.UpdateAvailable ? $"UPDATE: {r.InstalledVersion} -> {r.LatestVersion}" : $"up-to-date ({r.InstalledVersion})";
            Console.WriteLine($"  {r.AppName}: {status}");
        }

        if (csv)
        {
            Directory.CreateDirectory(output);
            var csvPath = Path.Combine(output, "version-check.csv");
            File.WriteAllText(csvPath, monitor.ExportResultsCsv(results));
            Console.WriteLine($"\nCSV exported to {csvPath}");
        }

        if (updates.Count > 0)
        {
            var fixlets = monitor.GenerateUpgradeFixlets(results);
            Directory.CreateDirectory(output);
            var generated = new List<string>();

            foreach (var fixlet in fixlets)
            {
                var fileName = FixletFactory.SanitizeFileName(fixlet.Title) + ".bes";
                var filePath = Path.Combine(output, fileName);
                writer.WriteBes(filePath, fixlet);
                generated.Add(filePath);
            }

            Console.WriteLine($"\n{fixlets.Count} upgrade fixlet(s) generated in {Path.GetFullPath(output)}");

            if (importToConsole)
                await ImportAllAsync(sp, generated, site);
        }
    }

    private static async Task RunFetch(IServiceProvider sp, string? query, string? id)
    {
        var winget = sp.GetRequiredService<IWingetIntegration>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("fetch");

        if (!winget.IsAvailable())
        {
            Console.WriteLine("Error: winget is not installed or not available.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(id))
        {
            Console.WriteLine($"Fetching details for {id}...");
            var details = await winget.GetDetailsAsync(id);
            if (details is null)
            {
                Console.WriteLine("Package not found.");
                return;
            }
            Console.WriteLine($"  Name:           {details.Name}");
            Console.WriteLine($"  Version:        {details.Version}");
            Console.WriteLine($"  Publisher:      {details.Publisher}");
            Console.WriteLine($"  Description:    {details.Description}");
            Console.WriteLine($"  Installer URL:  {details.InstallerUrl}");
            Console.WriteLine($"  Installer Type: {details.InstallerType}");
            Console.WriteLine($"  Silent Args:    {details.SilentArgs}");
            Console.WriteLine($"  Uninstall Args: {details.UninstallArgs}");
        }
        else if (!string.IsNullOrWhiteSpace(query))
        {
            Console.WriteLine($"Searching for '{query}'...");
            var results = await winget.SearchAsync(query);
            if (results.Count == 0)
            {
                Console.WriteLine("No results found.");
                return;
            }
            Console.WriteLine($"\nFound {results.Count} result(s):\n");
            foreach (var pkg in results)
                Console.WriteLine($"  {pkg.Id,-40} {pkg.Version,-20} {pkg.Name}");
        }
        else
        {
            Console.WriteLine("Specify --query or --id.");
        }
    }

    private static async Task RunBatch(IServiceProvider sp, string input, string output,
        string? type, bool importToConsole, string? site)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("batch");
        var writer = sp.GetRequiredService<IFixletWriter>();

        if (!File.Exists(input))
        {
            Console.WriteLine($"Error: File not found: {input}");
            return;
        }

        Console.WriteLine($"Loading {input}...");
        var ext = Path.GetExtension(input).ToLowerInvariant();
        List<string[]> rows;

        if (ext == ".csv")
        {
            rows = CsvReader.Parse(File.ReadAllText(input));
        }
        else
        {
            using var wb = new ClosedXML.Excel.XLWorkbook(input);
            var ws = wb.Worksheets.First();
            var range = ws.RangeUsed();
            if (range is null)
            {
                Console.WriteLine("Error: Excel file is empty.");
                return;
            }
            rows = new List<string[]>();
            for (int r = 1; r <= range.RowCount(); r++)
            {
                var row = new string[range.ColumnCount()];
                for (int c = 1; c <= range.ColumnCount(); c++)
                    row[c - 1] = range.Cell(r, c).GetString();
                rows.Add(row);
            }
        }

        if (rows.Count < 2)
        {
            Console.WriteLine("Error: File has no data rows.");
            return;
        }

        var headers = rows[0];
        var dataRows = rows.Skip(1).ToList();
        var map = FixletFactory.BuildColumnMap(headers);

        Console.WriteLine($"Loaded {dataRows.Count} data rows. Generating fixlets...\n");

        Directory.CreateDirectory(output);
        int ok = 0, skipped = 0;
        var generated = new List<string>();

        for (int i = 0; i < dataRows.Count; i++)
        {
            try
            {
                var model = FixletFactory.Build(dataRows[i], map, type, out var issues);
                foreach (var issue in issues)
                    Console.WriteLine($"  [WARN] Row {i + 2}: {issue}");

                if (string.IsNullOrWhiteSpace(model.Title))
                {
                    skipped++;
                    continue;
                }

                var fileName = FixletFactory.SanitizeFileName(model.Title) + ".bes";
                var filePath = Path.Combine(output, fileName);
                writer.WriteBes(filePath, model);
                generated.Add(filePath);
                ok++;
                Console.WriteLine($"  [{ok}] {model.Title}");
            }
            catch (Exception ex)
            {
                skipped++;
                Console.WriteLine($"  [SKIP] Row {i + 2}: {ex.Message}");
            }
        }

        Console.WriteLine($"\nDone. {ok} created, {skipped} skipped. Output: {Path.GetFullPath(output)}");

        if (importToConsole)
            await ImportAllAsync(sp, generated, site);
    }

    private static async Task RunCreate(IServiceProvider sp, string? name, string? version,
        string type, string? url, string? path, string output, string? silentArgs, bool auto,
        bool importToConsole, string? site)
    {
        var winget = sp.GetRequiredService<IWingetIntegration>();
        var scanner = sp.GetRequiredService<IInstalledAppsScanner>();
        var writer = sp.GetRequiredService<IFixletWriter>();
        var downloadService = sp.GetRequiredService<IDownloadService>();

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("Error: --name is required.");
            return;
        }

        var effectiveName = name;
        var effectiveVersion = version ?? "";
        var effectiveUrl = url ?? "";
        var effectivePath = path ?? "";
        var effectiveSilentArgs = silentArgs ?? "";
        var effectiveSha1 = "";
        var effectiveSha256 = "";
        long effectiveFileSize = 0;

        if (auto && winget.IsAvailable())
        {
            Console.WriteLine($"Fetching metadata for '{name}' from winget...");
            var searchResults = await winget.SearchAsync(name);
            var bestMatch = searchResults.FirstOrDefault();
            if (bestMatch is not null)
            {
                var details = await winget.GetDetailsAsync(bestMatch.Id);
                if (details is not null)
                {
                    effectiveUrl = details.InstallerUrl;
                    effectiveSilentArgs = details.SilentArgs;
                    effectiveVersion = details.Version;
                    effectivePath = details.InstallLocation;
                    effectiveSha1 = details.Sha1;
                    effectiveSha256 = details.Sha256;
                    effectiveFileSize = details.FileSizeBytes ?? 0;
                    Console.WriteLine($"  Auto-detected: {bestMatch.Id} v{details.Version}");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(effectiveSilentArgs))
            effectiveSilentArgs = SilentArgsDatabase.GetForApp(effectiveName);

        // Fetch hashes if URL is provided but hashes are missing
        if (!string.IsNullOrWhiteSpace(effectiveUrl) && (string.IsNullOrWhiteSpace(effectiveSha1) || string.IsNullOrWhiteSpace(effectiveSha256) || effectiveFileSize == 0))
        {
            Console.WriteLine("Computing file hashes (this may take a moment)...");
            var hashInfo = await downloadService.GetFileHashInfoAsync(effectiveUrl);
            if (hashInfo is not null)
            {
                if (string.IsNullOrWhiteSpace(effectiveSha1)) effectiveSha1 = hashInfo.Sha1;
                if (string.IsNullOrWhiteSpace(effectiveSha256)) effectiveSha256 = hashInfo.Sha256;
                if (effectiveFileSize == 0) effectiveFileSize = hashInfo.FileSizeBytes;
                Console.WriteLine($"  SHA1:    {effectiveSha1}");
                Console.WriteLine($"  SHA256:  {effectiveSha256}");
                Console.WriteLine($"  Size:    {effectiveFileSize} bytes");
            }
        }

        var template = FixletTemplates.Apply(type, effectiveName, effectiveVersion, effectiveUrl, effectiveSilentArgs, effectivePath, "",
            sha1: effectiveSha1, sha256: effectiveSha256, fileSizeBytes: effectiveFileSize);
        var relevance = RelevanceBuilder.BuildFromInstalledApp(
            new InstalledApp { Name = effectiveName, Version = effectiveVersion, InstallLocation = effectivePath },
            type);

        var model = new FixletModel
        {
            Title = template.Title,
            Category = "Applications",
            Source = "FixletBuilder CLI",
            SourceId = name,
            Relevance = relevance,
            Description = template.Description,
            ActionDescription = template.ActionDescription,
            ActionScript = template.ActionScript,
            SuccessCriteria = RelevanceBuilder.BuildSuccessCriteria(effectivePath, type, effectiveVersion),
            Sha1 = effectiveSha1,
            Sha256 = effectiveSha256,
            FileSizeBytes = effectiveFileSize > 0 ? effectiveFileSize : null
        };

        Directory.CreateDirectory(output);
        var fileName = FixletFactory.SanitizeFileName(model.Title) + ".bes";
        var filePath = Path.Combine(output, fileName);
        writer.WriteBes(filePath, model);

        Console.WriteLine($"Created: {filePath}");
        Console.WriteLine($"  Title:    {model.Title}");
        Console.WriteLine($"  Type:     {type}");
        Console.WriteLine($"  URL:      {effectiveUrl}");
        Console.WriteLine($"  Args:     {effectiveSilentArgs}");
        if (!string.IsNullOrWhiteSpace(effectiveSha256))
            Console.WriteLine($"  SHA256:   {effectiveSha256}");
        if (effectiveFileSize > 0)
            Console.WriteLine($"  Size:     {effectiveFileSize} bytes");

        if (importToConsole)
            await ImportAllAsync(sp, new[] { filePath }, site);
    }

    private static async Task RunPing(IServiceProvider sp)
    {
        var client = sp.GetService<IBigFixConsoleClient>();
        if (client is null)
        {
            Console.WriteLine("BigFix console is not configured. Add a 'BigFix' section to appsettings.json or environment variables.");
            return;
        }

        var ok = await client.PingAsync();
        Console.WriteLine(ok ? "BigFix console: REACHABLE" : "BigFix console: UNREACHABLE");
    }

    private static async Task ImportAllAsync(IServiceProvider sp, IEnumerable<string> files, string? site)
    {
        var client = sp.GetService<IBigFixConsoleClient>();
        if (client is null)
        {
            Console.WriteLine("\n[!] BigFix console client not configured. Skipping import.");
            Console.WriteLine("    Add a 'BigFix' section to appsettings.json with BaseUrl/Username/Password.");
            return;
        }

        Console.WriteLine($"\nImporting {files.Count()} fixlet(s) into BigFix console (site: {site ?? "master"})...");
        int success = 0, failed = 0;

        foreach (var file in files)
        {
            try
            {
                var result = await client.ImportFixletAsync(file, site);
                if (result.Success)
                {
                    success++;
                    Console.WriteLine($"  [OK] {Path.GetFileName(file)}" +
                        (result.FixletId.HasValue ? $" -> ID {result.FixletId}" : ""));
                }
                else
                {
                    failed++;
                    Console.WriteLine($"  [FAIL] {Path.GetFileName(file)}: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  [ERROR] {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        Console.WriteLine($"\nImport complete: {success} succeeded, {failed} failed.");
    }

    private static FixletModel BuildModelFromApp(InstalledApp app, string type)
    {
        var template = FixletTemplates.Apply(type, app.Name, app.Version, "",
            SilentArgsDatabase.GetForApp(app.Name), app.InstallLocation, app.UninstallString);
        var relevance = RelevanceBuilder.BuildFromInstalledApp(app, type);

        return new FixletModel
        {
            Title = template.Title,
            Category = "Applications",
            Source = "FixletBuilder Scan",
            SourceId = app.WingetId,
            Relevance = relevance,
            Description = template.Description,
            ActionDescription = template.ActionDescription,
            ActionScript = template.ActionScript,
            SuccessCriteria = RelevanceBuilder.BuildSuccessCriteria(app.InstallLocation, type, app.Version)
        };
    }
}