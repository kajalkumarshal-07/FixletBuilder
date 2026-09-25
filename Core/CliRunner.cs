using System.CommandLine;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using FixletBuilder.Core.PatchFactory;
using Microsoft.Extensions.Configuration;
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
        var scanDetection = new Option<string>(new[] { "--detection" }, () => "auto",
            "Detection method: auto, msi, file, registry, displayname, path");
        scanCommand.AddOption(scanOutput); scanCommand.AddOption(scanType);
        scanCommand.AddOption(scanFilter); scanCommand.AddOption(scanExclude);
        scanCommand.AddOption(scanFormat); scanCommand.AddOption(scanImport); scanCommand.AddOption(scanSite);
        scanCommand.AddOption(scanDetection);
        scanCommand.SetHandler(ctx =>
        {
            var o = ctx.ParseResult.GetValueForOption(scanOutput) ?? "./fixlet-output";
            var t = ctx.ParseResult.GetValueForOption(scanType) ?? "install";
            var f = ctx.ParseResult.GetValueForOption(scanFilter);
            var e = ctx.ParseResult.GetValueForOption(scanExclude);
            var fmt = ctx.ParseResult.GetValueForOption(scanFormat) ?? "bes";
            var imp = ctx.ParseResult.GetValueForOption(scanImport);
            var site = ctx.ParseResult.GetValueForOption(scanSite);
            var det = ctx.ParseResult.GetValueForOption(scanDetection) ?? "auto";
            RunScanAsync(services, o, t, f, e, fmt, imp, site, det);
        });

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
        var batchDetection = new Option<string>(new[] { "--detection" }, () => "auto",
            "Fallback detection method when a row has no 'detection' column: auto, msi, file, registry, displayname, path");
        batchCommand.AddOption(batchInput); batchCommand.AddOption(batchOutput);
        batchCommand.AddOption(batchType); batchCommand.AddOption(batchImport);
        batchCommand.AddOption(batchSite); batchCommand.AddOption(batchDetection);
        batchCommand.SetHandler(ctx =>
        {
            var i = ctx.ParseResult.GetValueForOption(batchInput) ?? "";
            var o = ctx.ParseResult.GetValueForOption(batchOutput) ?? "./fixlet-output";
            var t = ctx.ParseResult.GetValueForOption(batchType);
            var imp = ctx.ParseResult.GetValueForOption(batchImport);
            var site = ctx.ParseResult.GetValueForOption(batchSite);
            var det = ctx.ParseResult.GetValueForOption(batchDetection) ?? "auto";
            RunBatchAsync(services, i, o, t, imp, site, det);
        });

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
        var createDetection = new Option<string>(new[] { "--detection" }, () => "auto",
            "Detection method: auto, msi, file, registry, displayname, path");
        var createMsiCode = new Option<string?>(new[] { "--msi-code" }, "MSI product code {GUID} (for --detection msi)");
        var createRegKey = new Option<string?>(new[] { "--registry-key" }, "Registry uninstall key for detection");
        var createRegValueName = new Option<string?>(new[] { "--registry-value-name" }, "Registry value name (default DisplayVersion)");
        var createRegValue = new Option<string?>(new[] { "--registry-value" }, "Expected registry value / version");
        createCommand.AddOption(createName); createCommand.AddOption(createVersion);
        createCommand.AddOption(createType); createCommand.AddOption(createUrl);
        createCommand.AddOption(createPath); createCommand.AddOption(createOutput);
        createCommand.AddOption(createSilent); createCommand.AddOption(createAuto);
        createCommand.AddOption(createImport); createCommand.AddOption(createSite);
        createCommand.AddOption(createDetection); createCommand.AddOption(createMsiCode);
        createCommand.AddOption(createRegKey); createCommand.AddOption(createRegValueName);
        createCommand.AddOption(createRegValue);
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
            var detection = ctx.ParseResult.GetValueForOption(createDetection) ?? "auto";
            var msiCode = ctx.ParseResult.GetValueForOption(createMsiCode);
            var regKey = ctx.ParseResult.GetValueForOption(createRegKey);
            var regValueName = ctx.ParseResult.GetValueForOption(createRegValueName);
            var regValue = ctx.ParseResult.GetValueForOption(createRegValue);
            RunCreateAsync(services, n, v, t, u, p, o, s, auto, imp, site,
                detection, msiCode, regKey, regValueName, regValue);
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
        rootCommand.AddCommand(BuildAnalysisCommand(services));
        rootCommand.AddCommand(BuildPatchCommand(services));

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
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "FIXLETBUILDER_")
            .Build();

        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(config);
        sc.AddLogging(b => b.AddSerilog());
        sc.AddHttpClient<IDownloadService, DownloadService>();
        sc.AddHttpClient<UpdateCatalogClient>();
        sc.AddHttpClient(nameof(UpdateCatalogClient));
        sc.AddSingleton<IInstalledAppsScanner, InstalledAppsScanner>();
        sc.AddSingleton<IWingetIntegration, WingetIntegration>();
        sc.AddSingleton<IAppVersionMonitor, AppVersionMonitor>();
        sc.AddSingleton<IFixletWriter, FixletWriter>();
        sc.AddSingleton<IAnalysisWriter, AnalysisWriter>();
        sc.AddSingleton<IUpdateCatalogClient>(sp =>
            new UpdateCatalogClient(
                sp.GetRequiredService<ILogger<UpdateCatalogClient>>(),
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(UpdateCatalogClient))));
        sc.AddSingleton<IPatchValidator, PatchValidator>();
        sc.AddSingleton<IPatchPipeline>(sp =>
        {
            var bf = sp.GetService<IBigFixConsoleClient>();
            return new PatchPipeline(
                sp.GetRequiredService<IUpdateCatalogClient>(),
                sp.GetRequiredService<IPatchValidator>(),
                sp.GetRequiredService<IFixletWriter>(),
                sp.GetRequiredService<ILogger<PatchPipeline>>(),
                bf);
        });

        var bigFixSection = config.GetSection("BigFix");
        var baseUrl = bigFixSection["BaseUrl"];
        if (!string.IsNullOrWhiteSpace(baseUrl) &&
            !baseUrl.Contains("example.com", StringComparison.OrdinalIgnoreCase))
        {
            var options = new BigFixConsoleOptions
            {
                BaseUrl = baseUrl,
                Username = bigFixSection["Username"] ?? "",
                Password = bigFixSection["Password"] ?? "",
                TimeoutSeconds = int.TryParse(bigFixSection["TimeoutSeconds"], out var t) ? t : 30,
                MaxRetries = int.TryParse(bigFixSection["MaxRetries"], out var r) ? r : 3,
                SkipCertificateValidation = bool.TryParse(bigFixSection["SkipCertificateValidation"], out var skip) && skip
            };
            sc.AddSingleton(options);
            sc.AddSingleton<IBigFixConsoleClient>(sp =>
                new BigFixConsoleClient(
                    options,
                    sp.GetRequiredService<ILogger<BigFixConsoleClient>>()));
        }

        return sc.BuildServiceProvider();
    }

    private static Command BuildAnalysisCommand(IServiceProvider services)
    {
        var analysis = new Command("analysis", "Create and publish BigFix Analyses (retrieved-property content)");

        // templates
        var templates = new Command("templates", "List built-in analysis templates");
        templates.SetHandler(() =>
        {
            Console.WriteLine("Built-in analysis templates:\n");
            foreach (var key in AnalysisTemplates.Keys)
            {
                var t = AnalysisTemplates.Get(key, "ExampleApp");
                Console.WriteLine($"  {key,-22} {t.Title}  ({t.Properties.Count} properties)");
            }
            Console.WriteLine("\nUsage: FixletBuilder.exe analysis create --template app-status --name \"Chrome\" -o ./analysis-output");
        });

        // create
        var create = new Command("create", "Create an analysis .bes file");
        var createTemplate = new Option<string>(new[] { "-t", "--template" }, () => "app-status",
            "Template: app-status, app-version, software-inventory, os-overview, disk-space, bigfix-components, custom");
        var createName = new Option<string>(new[] { "-n", "--name" }, () => "", "Application / filter name");
        var createTitle = new Option<string?>(new[] { "--title" }, "Override analysis title");
        var createRelevance = new Option<string[]>(new[] { "-r", "--relevance" },
            "Targeting relevance (repeatable). Default: true");
        var createProperty = new Option<string[]>(new[] { "-p", "--property" },
            "Extra property as Name=relevance (repeatable)");
        var createRegKey = new Option<string?>(new[] { "--registry-key" }, "Registry key for detection templates");
        var createRegValueName = new Option<string?>(new[] { "--registry-value-name" }, "Registry value name (default DisplayVersion)");
        var createRegValue = new Option<string?>(new[] { "--registry-value" }, "Expected registry value");
        var createPath = new Option<string?>(new[] { "--path" }, "Install path for detection templates");
        var createPeriod = new Option<string?>(new[] { "--period" }, "Default EvaluationPeriod (e.g. PT1H, 1h, every report)");
        var createOutput = new Option<string>(new[] { "-o", "--output" }, () => "./analysis-output", "Output directory");
        var createWhatIf = new Option<bool>(new[] { "--what-if" }, "Print XML to stdout instead of writing a file");
        var createPublish = new Option<bool>(new[] { "--publish" }, "Publish to BigFix REST API after create");
        var createSite = new Option<string?>(new[] { "--site" }, "Target BigFix site for publish");

        create.AddOption(createTemplate); create.AddOption(createName); create.AddOption(createTitle);
        create.AddOption(createRelevance); create.AddOption(createProperty);
        create.AddOption(createRegKey); create.AddOption(createRegValueName); create.AddOption(createRegValue);
        create.AddOption(createPath); create.AddOption(createPeriod); create.AddOption(createOutput);
        create.AddOption(createWhatIf); create.AddOption(createPublish); create.AddOption(createSite);

        create.SetHandler(ctx =>
        {
            RunAnalysisCreate(services,
                    ctx.ParseResult.GetValueForOption(createTemplate) ?? "app-status",
                    ctx.ParseResult.GetValueForOption(createName) ?? "",
                    ctx.ParseResult.GetValueForOption(createTitle),
                    ctx.ParseResult.GetValueForOption(createRelevance) ?? Array.Empty<string>(),
                    ctx.ParseResult.GetValueForOption(createProperty) ?? Array.Empty<string>(),
                    ctx.ParseResult.GetValueForOption(createRegKey),
                    ctx.ParseResult.GetValueForOption(createRegValueName),
                    ctx.ParseResult.GetValueForOption(createRegValue),
                    ctx.ParseResult.GetValueForOption(createPath),
                    ctx.ParseResult.GetValueForOption(createPeriod),
                    ctx.ParseResult.GetValueForOption(createOutput) ?? "./analysis-output",
                    ctx.ParseResult.GetValueForOption(createWhatIf),
                    ctx.ParseResult.GetValueForOption(createPublish),
                    ctx.ParseResult.GetValueForOption(createSite))
                .GetAwaiter().GetResult();
        });

        // publish (existing .bes containing <Analysis>)
        var publish = new Command("publish", "Publish analysis .bes XML to BigFix REST api/analyses");
        var pubInput = new Option<string>(new[] { "-i", "--input" }, "Path to analysis .bes file (required)");
        var pubSite = new Option<string?>(new[] { "--site" }, "Target site name");
        var pubWhatIf = new Option<bool>(new[] { "--what-if" }, "Print XML without POSTing");
        publish.AddOption(pubInput); publish.AddOption(pubSite); publish.AddOption(pubWhatIf);
        publish.SetHandler(ctx =>
        {
            var code = RunAnalysisPublish(services,
                    ctx.ParseResult.GetValueForOption(pubInput) ?? "",
                    ctx.ParseResult.GetValueForOption(pubSite),
                    ctx.ParseResult.GetValueForOption(pubWhatIf))
                .GetAwaiter().GetResult();
            Environment.ExitCode = code;
        });

        analysis.AddCommand(templates);
        analysis.AddCommand(create);
        analysis.AddCommand(publish);
        return analysis;
    }

    private static Task RunAnalysisCreate(IServiceProvider sp, string template, string name, string? title,
        string[] relevance, string[] properties, string? regKey, string? regValueName, string? regValue,
        string? path, string? period, string output, bool whatIf, bool publish, string? site) =>
        RunAnalysisCreateAsync(sp, template, name, title, relevance, properties, regKey, regValueName,
            regValue, path, period, output, whatIf, publish, site);

    private static async Task RunAnalysisCreateAsync(IServiceProvider sp, string template, string name, string? title,
        string[] relevance, string[] properties, string? regKey, string? regValueName, string? regValue,
        string? path, string? period, string output, bool whatIf, bool publish, string? site)
    {
        var writer = sp.GetRequiredService<IAnalysisWriter>();

        var t = AnalysisTemplates.Get(template, name, regKey ?? "", regValueName ?? "", regValue ?? "", path ?? "");

        var model = new AnalysisModel
        {
            Title = string.IsNullOrWhiteSpace(title) ? t.Title : title!,
            Description = t.Description,
            Category = t.Category,
            Source = "FixletBuilder CLI",
            SourceId = string.IsNullOrWhiteSpace(name) ? t.Key : name,
            Domain = "BESC",
            Relevance = relevance is { Length: > 0 }
                ? relevance.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList()
                : t.Relevance.ToList(),
            Properties = t.Properties.Select(p => new AnalysisProperty
            {
                Name = p.Name,
                Relevance = p.Relevance,
                EvaluationPeriod = !string.IsNullOrWhiteSpace(period)
                    ? AnalysisWriter.NormalizeEvaluationPeriod(period)
                    : p.EvaluationPeriod,
                KeepStatistics = p.KeepStatistics,
                Id = p.Id
            }).ToList()
        };

        foreach (var extra in properties ?? Array.Empty<string>())
        {
            var idx = extra.IndexOf('=');
            if (idx <= 0)
            {
                Console.WriteLine($"  [WARN] Ignoring --property (expected Name=relevance): {extra}");
                continue;
            }
            var pname = extra[..idx].Trim();
            var prel = extra[(idx + 1)..].Trim();
            if (pname.Length > 0 && prel.Length > 0)
            {
                model.Properties.Add(new AnalysisProperty
                {
                    Name = pname,
                    Relevance = prel,
                    EvaluationPeriod = AnalysisWriter.NormalizeEvaluationPeriod(period ?? "")
                });
            }
        }

        var issues = writer.Validate(model);
        foreach (var issue in issues)
            Console.WriteLine($"  [WARN] {issue}");

        string xml;
        try
        {
            xml = writer.GenerateXml(model);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Failed to generate analysis XML: " + ex.Message);
            Environment.ExitCode = 3;
            return;
        }

        if (whatIf)
        {
            Console.WriteLine(xml);
            return;
        }

        Directory.CreateDirectory(output);
        var filePath = Path.Combine(output, FixletFactory.SanitizeFileName(model.Title) + ".bes");
        writer.WriteBes(filePath, model);

        Console.WriteLine($"Created analysis: {filePath}");
        Console.WriteLine($"  Title:     {model.Title}");
        Console.WriteLine($"  Template:  {template}");
        Console.WriteLine($"  Target:    {string.Join(" AND ", model.Relevance)}");
        Console.WriteLine($"  Properties:{model.Properties.Count}");
        foreach (var p in model.Properties)
            Console.WriteLine($"    - {p.Name}  [{(string.IsNullOrWhiteSpace(p.EvaluationPeriod) ? "every report" : p.EvaluationPeriod)}]");

        if (publish)
            await PublishAnalysisXmlAsync(sp, xml, site, whatIf: false);
    }

    private static async Task<int> RunAnalysisPublish(IServiceProvider sp, string input, string? site, bool whatIf)
    {
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            Console.Error.WriteLine("Error: --input file not found.");
            return 3;
        }

        var xml = await File.ReadAllTextAsync(input);
        if (!xml.Contains("<Analysis", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Error: file does not contain a <Analysis> element.");
            return 3;
        }

        if (whatIf)
        {
            Console.WriteLine(xml);
            return 0;
        }

        return await PublishAnalysisXmlAsync(sp, xml, site, whatIf: false) ? 0 : 3;
    }

    private static async Task<bool> PublishAnalysisXmlAsync(IServiceProvider sp, string xml, string? site, bool whatIf)
    {
        if (whatIf)
        {
            Console.WriteLine(xml);
            return true;
        }

        var client = sp.GetService<IBigFixConsoleClient>();
        if (client is null)
        {
            Console.WriteLine("\n[!] BigFix console client not configured - cannot publish analysis.");
            Console.WriteLine("    Add a 'BigFix' section to appsettings.json with BaseUrl/Username/Password.");
            return false;
        }

        Console.WriteLine($"\nPublishing analysis to BigFix (site: {site ?? "default"})...");
        var result = await client.ImportAnalysisAsync(xml, site);
        if (result.Success)
        {
            Console.WriteLine($"  [OK] Analysis published" +
                (result.FixletId.HasValue ? $" -> ID {result.FixletId}" : "") +
                (result.Message.Length > 0 ? $"\n  {Truncate(result.Message, 200)}" : ""));
            return true;
        }

        Console.WriteLine($"  [FAIL] {result.Message}");
        return false;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";

    private static Command BuildPatchCommand(IServiceProvider services)
    {
        var patchCommand = new Command("patch", "Monthly Microsoft patch automation (discovery -> validation -> BES -> publish -> deploy -> compliance)");

        var discover = new Command("discover", "Discover patches from the Microsoft Update Catalog");
        var discoverKb = new Option<string[]>(new[] { "-k", "--kb" }, "KB number(s), e.g. KB5071234");
        var discoverQuery = new Option<string?>(new[] { "-q", "--query" }, "Free-text catalog query");
        var discoverProduct = new Option<string?>(new[] { "-p", "--product" }, "Product filter, e.g. 'Windows 11 23H2'");
        var discoverAfter = new Option<string?>(new[] { "--after" }, "Only updates released on/after yyyy-MM-dd");
        var discoverTuesday = new Option<bool>(new[] { "--patch-tuesday", "--this-month" }, "Auto-scan this month's Win10 + Win11 catalog updates");
        var discoverWin10 = new Option<bool>(new[] { "--win10" }, "Include Windows 10 in month auto-scan");
        var discoverWin11 = new Option<bool>(new[] { "--win11" }, "Include Windows 11 in month auto-scan");
        var discoverArch = new Option<string>(new[] { "--arch" }, () => "x64", "Architecture: x64, x86, arm64");
        var discoverMax = new Option<int>(new[] { "--max" }, () => 80, "Max catalog results");
        var discoverOut = new Option<string>(new[] { "-o", "--output" }, () => "./patch-output", "Output directory for patches.json");
        discover.AddOption(discoverKb); discover.AddOption(discoverQuery); discover.AddOption(discoverProduct);
        discover.AddOption(discoverAfter); discover.AddOption(discoverTuesday);
        discover.AddOption(discoverWin10); discover.AddOption(discoverWin11);
        discover.AddOption(discoverArch);
        discover.AddOption(discoverMax); discover.AddOption(discoverOut);
        discover.SetHandler(ctx =>
        {
            var win10 = ctx.ParseResult.GetValueForOption(discoverWin10);
            var win11 = ctx.ParseResult.GetValueForOption(discoverWin11);
            var month = ctx.ParseResult.GetValueForOption(discoverTuesday);
            var req = new DiscoverRequest
            {
                KbNumbers = (ctx.ParseResult.GetValueForOption(discoverKb) ?? Array.Empty<string>()).ToList(),
                Query = ctx.ParseResult.GetValueForOption(discoverQuery),
                ProductFilter = ctx.ParseResult.GetValueForOption(discoverProduct),
                PatchTuesday = month,
                ScanThisMonth = month,
                ScanWindows10 = win10 || month,
                ScanWindows11 = win11 || month,
                Architecture = ctx.ParseResult.GetValueForOption(discoverArch) ?? "x64",
                MaxResults = ctx.ParseResult.GetValueForOption(discoverMax)
            };
            var after = ctx.ParseResult.GetValueForOption(discoverAfter);
            if (!string.IsNullOrWhiteSpace(after) && DateTime.TryParse(after, out var dt))
                req.ReleasedAfter = dt;
            else if (req.WantsMonthScan)
                req.ReleasedAfter = UpdateCatalogClient.LastPatchTuesday().AddDays(-1);

            RunPatchDiscover(services, req, ctx.ParseResult.GetValueForOption(discoverOut) ?? "./patch-output")
                .GetAwaiter().GetResult();
        });

        var validate = new Command("validate", "Run the validation gate before generating/publishing");
        var validateIn = new Option<string>(new[] { "-i", "--input" }, () => "./patch-output/patches.json", "patches.json path");
        var validateDl = new Option<bool>(new[] { "--download" }, "Download installers to verify size/SHA256");
        validate.AddOption(validateIn); validate.AddOption(validateDl);
        validate.SetHandler(ctx =>
        {
            var report = RunPatchValidate(services,
                    ctx.ParseResult.GetValueForOption(validateIn)!,
                    ctx.ParseResult.GetValueForOption(validateDl))
                .GetAwaiter().GetResult();
            Environment.ExitCode = report.AllPassed ? 0 : 3;
        });

        var generate = new Command("generate", "Generate .bes Fixlets (or Tasks) from patches.json");
        var genIn = new Option<string>(new[] { "-i", "--input" }, () => "./patch-output/patches.json", "patches.json path");
        var genOut = new Option<string>(new[] { "-o", "--output" }, () => "./fixlet-output", "Directory for .bes files");
        var genTask = new Option<bool>(new[] { "--task" }, "Generate Tasks instead of Fixlets");
        generate.AddOption(genIn); generate.AddOption(genOut); generate.AddOption(genTask);
        generate.SetHandler(ctx =>
        {
            RunPatchGenerate(services,
                    ctx.ParseResult.GetValueForOption(genIn)!,
                    ctx.ParseResult.GetValueForOption(genOut)!,
                    ctx.ParseResult.GetValueForOption(genTask))
                .GetAwaiter().GetResult();
        });

        var publish = new Command("publish", "Publish generated content to a BigFix site (dedupe by KB)");
        var pubIn = new Option<string>(new[] { "-i", "--input" }, () => "./patch-output/patches.json", "patches.json path");
        var pubOut = new Option<string>(new[] { "-o", "--output" }, () => "./patch-output", "State directory");
        var pubType = new Option<string>(new[] { "--site-type" }, () => "custom", "BigFix site type");
        var pubSite = new Option<string>(new[] { "--site" }, () => "Enterprise Windows Patching", "BigFix site name");
        var pubWhatIf = new Option<bool>(new[] { "--what-if" }, "Print actions without publishing");
        publish.AddOption(pubIn); publish.AddOption(pubOut); publish.AddOption(pubType);
        publish.AddOption(pubSite); publish.AddOption(pubWhatIf);
        publish.SetHandler(ctx =>
        {
            var code = RunPatchPublish(services,
                    ctx.ParseResult.GetValueForOption(pubIn)!,
                    ctx.ParseResult.GetValueForOption(pubOut)!,
                    ctx.ParseResult.GetValueForOption(pubType)!,
                    ctx.ParseResult.GetValueForOption(pubSite)!,
                    ctx.ParseResult.GetValueForOption(pubWhatIf))
                .GetAwaiter().GetResult();
            Environment.ExitCode = code;
        });

        var groups = new Command("groups", "Create/update the monthly automatic group hierarchy");
        var grpIn = new Option<string>(new[] { "-i", "--input" }, () => "./patch-output/patches.json", "patches.json path");
        var grpOut = new Option<string>(new[] { "-o", "--output" }, () => "./patch-output", "State directory");
        var grpCycle = new Option<string>(new[] { "--cycle" }, () => "", "Cycle id, e.g. 2026-09 (default: from patch dates)");
        var grpWhatIf = new Option<bool>(new[] { "--what-if" }, "Print actions without creating groups");
        groups.AddOption(grpIn); groups.AddOption(grpOut); groups.AddOption(grpCycle); groups.AddOption(grpWhatIf);
        groups.SetHandler(ctx =>
        {
            RunPatchGroups(services,
                    ctx.ParseResult.GetValueForOption(grpIn)!,
                    ctx.ParseResult.GetValueForOption(grpOut)!,
                    ctx.ParseResult.GetValueForOption(grpCycle)!,
                    ctx.ParseResult.GetValueForOption(grpWhatIf))
                .GetAwaiter().GetResult();
        });

        var deploy = new Command("deploy", "Start staged deployment actions (pilot -> wave1 -> wave2 -> wave3)");
        var depIn = new Option<string>(new[] { "-i", "--input" }, () => "./patch-output/patches.json", "patches.json path");
        var depOut = new Option<string>(new[] { "-o", "--output" }, () => "./patch-output", "State directory");
        var depStage = new Option<string>(new[] { "-s", "--stage" }, () => PatchStages.Pilot, "Stage: pilot, wave1, wave2, wave3");
        var depWhatIf = new Option<bool>(new[] { "--what-if" }, "Print actions without deploying");
        deploy.AddOption(depIn); deploy.AddOption(depOut); deploy.AddOption(depStage); deploy.AddOption(depWhatIf);
        deploy.SetHandler(ctx =>
        {
            var code = RunPatchDeploy(services,
                    ctx.ParseResult.GetValueForOption(depIn)!,
                    ctx.ParseResult.GetValueForOption(depOut)!,
                    ctx.ParseResult.GetValueForOption(depStage)!,
                    ctx.ParseResult.GetValueForOption(depWhatIf))
                .GetAwaiter().GetResult();
            Environment.ExitCode = code;
        });

        var compliance = new Command("compliance", "Generate a compliance report for the current cycle");
        var compIn = new Option<string>(new[] { "-i", "--input" }, () => "./patch-output/patches.json", "patches.json path");
        var compOut = new Option<string>(new[] { "-o", "--output" }, () => "./patch-output", "State directory");
        var compReport = new Option<string>(new[] { "-r", "--report" }, () => "./patch-output/compliance.json", "Report path");
        compliance.AddOption(compIn); compliance.AddOption(compOut); compliance.AddOption(compReport);
        compliance.SetHandler(ctx =>
        {
            RunPatchCompliance(services,
                    ctx.ParseResult.GetValueForOption(compIn)!,
                    ctx.ParseResult.GetValueForOption(compOut)!,
                    ctx.ParseResult.GetValueForOption(compReport)!)
                .GetAwaiter().GetResult();
        });

        var cycle = new Command("cycle", "Run discover -> validate -> generate for Win10 + Win11 this month");
        var cycOut = new Option<string>(new[] { "-o", "--output" }, () => "./patch-output", "Output directory");
        var cycArch = new Option<string>(new[] { "--arch" }, () => "x64", "Architecture");
        var cycDownload = new Option<bool>(new[] { "--download" }, "Download to verify hashes during validation");
        var cycKb = new Option<string[]>(new[] { "-k", "--kb" }, "Optional explicit KB list (skips month auto-scan)");
        cycle.AddOption(cycOut); cycle.AddOption(cycArch); cycle.AddOption(cycDownload); cycle.AddOption(cycKb);
        cycle.SetHandler(ctx =>
        {
            RunPatchCycle(services,
                    ctx.ParseResult.GetValueForOption(cycOut)!,
                    ctx.ParseResult.GetValueForOption(cycArch)!,
                    ctx.ParseResult.GetValueForOption(cycDownload),
                    ctx.ParseResult.GetValueForOption(cycKb) ?? Array.Empty<string>())
                .GetAwaiter().GetResult();
        });

        patchCommand.AddCommand(discover);
        patchCommand.AddCommand(validate);
        patchCommand.AddCommand(generate);
        patchCommand.AddCommand(publish);
        patchCommand.AddCommand(groups);
        patchCommand.AddCommand(deploy);
        patchCommand.AddCommand(compliance);
        patchCommand.AddCommand(cycle);
        return patchCommand;
    }

    private static Task RunPatchDiscover(IServiceProvider sp, DiscoverRequest request, string output) =>
        sp.GetRequiredService<IPatchPipeline>().DiscoverAsync(request, output);

    private static async Task<PatchValidationResult> RunPatchValidate(IServiceProvider sp, string input, bool download) =>
        await sp.GetRequiredService<IPatchPipeline>().ValidateAsync(input, download);

    private static Task RunPatchGenerate(IServiceProvider sp, string input, string output, bool asTask) =>
        sp.GetRequiredService<IPatchPipeline>().GenerateAsync(input, output, asTask);

    private static async Task<int> RunPatchPublish(IServiceProvider sp, string input, string output,
        string siteType, string siteName, bool whatIf) =>
        await sp.GetRequiredService<IPatchPipeline>().PublishAsync(input, output, siteType, siteName, whatIf);

    private static Task RunPatchGroups(IServiceProvider sp, string input, string output, string cycle, bool whatIf) =>
        sp.GetRequiredService<IPatchPipeline>().EnsureGroupsAsync(input, output, cycle, whatIf);

    private static async Task<int> RunPatchDeploy(IServiceProvider sp, string input, string output,
        string stage, bool whatIf) =>
        await sp.GetRequiredService<IPatchPipeline>().DeployAsync(input, output, stage, whatIf);

    private static Task RunPatchCompliance(IServiceProvider sp, string input, string output, string report) =>
        sp.GetRequiredService<IPatchPipeline>().ComplianceAsync(input, output, report);

    private static async Task RunPatchCycle(IServiceProvider sp, string output, string arch, bool download, string[] kbs)
    {
        var pipeline = sp.GetRequiredService<IPatchPipeline>();
        var request = new DiscoverRequest
        {
            Architecture = arch,
            MaxResults = 40
        };

        if (kbs.Length > 0)
        {
            request.KbNumbers = kbs.ToList();
        }
        else
        {
            request.PatchTuesday = true;
            request.ScanThisMonth = true;
            request.ScanWindows10 = true;
            request.ScanWindows11 = true;
            request.ReleasedAfter = UpdateCatalogClient.LastPatchTuesday().AddDays(-1);
        }

        Console.WriteLine("=== Patch cycle: discover ===");
        await pipeline.DiscoverAsync(request, output);

        var patchesPath = Path.Combine(output, "patches.json");
        Console.WriteLine("\n=== Patch cycle: validate ===");
        var report = await pipeline.ValidateAsync(patchesPath, download);
        if (!report.AllPassed)
        {
            Console.WriteLine("\nValidation gate FAILED - not generating content.");
            Environment.ExitCode = 3;
            return;
        }

        Console.WriteLine("\n=== Patch cycle: generate ===");
        await pipeline.GenerateAsync(patchesPath, Path.Combine(output, "bes"), asTask: false);
        Console.WriteLine("\nNext steps (human approval gate):");
        Console.WriteLine($"  FixletBuilder patch publish -i {patchesPath} -o {output} --site \"Enterprise Windows Patching\"");
        Console.WriteLine($"  FixletBuilder patch groups  -i {patchesPath} -o {output}");
        Console.WriteLine($"  FixletBuilder patch deploy  -i {patchesPath} -o {output} --stage pilot");
    }

    private static void RunScanAsync(IServiceProvider sp, string output, string type,
        string[]? filter, string[]? exclude, string format, bool importToConsole, string? site,
        string detection = "auto") =>
        RunScan(sp, output, type, filter, exclude, format, importToConsole, site, detection).GetAwaiter().GetResult();

    private static void RunMonitorAsync(IServiceProvider sp, string output, string[]? apps,
        string[]? track, string[]? untrack, bool csv, bool importToConsole, string? site) =>
        RunMonitor(sp, output, apps, track, untrack, csv, importToConsole, site).GetAwaiter().GetResult();

    private static void RunFetchAsync(IServiceProvider sp, string? query, string? id) =>
        RunFetch(sp, query, id).GetAwaiter().GetResult();

    private static void RunBatchAsync(IServiceProvider sp, string input, string output,
        string? type, bool importToConsole, string? site, string detection = "auto") =>
        RunBatch(sp, input, output, type, importToConsole, site, detection).GetAwaiter().GetResult();

    private static void RunCreateAsync(IServiceProvider sp, string? name, string? version,
        string type, string? url, string? path, string output, string? silentArgs, bool auto,
        bool importToConsole, string? site,
        string detection = "auto", string? msiCode = null, string? regKey = null,
        string? regValueName = null, string? regValue = null) =>
        RunCreate(sp, name, version, type, url, path, output, silentArgs, auto, importToConsole, site,
                detection, msiCode, regKey, regValueName, regValue)
            .GetAwaiter().GetResult();

    private static async Task RunScan(IServiceProvider sp, string output, string type,
        string[]? filter, string[]? exclude, string format, bool importToConsole, string? site,
        string detection = "auto")
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
                var model = BuildModelFromApp(app, type, DetectionMethods.Parse(detection));
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
        string? type, bool importToConsole, string? site, string detection = "auto")
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
                var model = FixletFactory.Build(dataRows[i], map, type, out var issues,
                    DetectionMethods.Parse(detection));
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
        bool importToConsole, string? site,
        string detection = "auto", string? msiCode = null, string? regKey = null,
        string? regValueName = null, string? regValue = null)
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

        var detectionResult = RelevanceBuilder.BuildDetection(new DetectionInput
        {
            Type = type,
            Method = DetectionMethods.Parse(detection),
            AppName = effectiveName,
            Version = effectiveVersion,
            InstallPath = effectivePath,
            RegistryKeyPath = regKey ?? "",
            RegistryValueName = string.IsNullOrWhiteSpace(regValueName) ? "DisplayVersion" : regValueName!,
            RegistryValue = regValue ?? "",
            MsiProductCode = msiCode ?? ""
        });

        var model = new FixletModel
        {
            Title = template.Title,
            Category = "Applications",
            Source = "FixletBuilder CLI",
            SourceId = name,
            Relevance = detectionResult.Relevance,
            Description = template.Description,
            ActionDescription = template.ActionDescription,
            ActionScript = template.ActionScript,
            SuccessCriteria = detectionResult.SuccessCriteria,
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
        Console.WriteLine($"  Detect:   {DetectionMethods.Label(detectionResult.Method)}");
        if (detectionResult.Warning is not null)
            Console.WriteLine($"  Note:     {detectionResult.Warning}");
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

    private static FixletModel BuildModelFromApp(InstalledApp app, string type,
        DetectionMethod method = DetectionMethod.Auto)
    {
        var template = FixletTemplates.Apply(type, app.Name, app.Version, "",
            SilentArgsDatabase.GetForApp(app.Name), app.InstallLocation, app.UninstallString);
        var relevance = RelevanceBuilder.BuildFromInstalledApp(app, type, method);
        var successCriteria = RelevanceBuilder.BuildSuccessCriteriaFromInstalledApp(app, type, method);

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
            SuccessCriteria = successCriteria
        };
    }
}