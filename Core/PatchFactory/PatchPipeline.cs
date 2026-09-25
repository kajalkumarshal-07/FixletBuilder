using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FixletBuilder.Core.PatchFactory;

public interface IPatchPipeline
{
    Task<List<PatchDefinition>> DiscoverAsync(DiscoverRequest request, string outputDir, CancellationToken ct = default);
    Task<PatchValidationResult> ValidateAsync(string patchesPath, bool download, CancellationToken ct = default);
    Task<List<PatchDefinition>> GenerateAsync(string patchesPath, string outputDir, bool asTask, CancellationToken ct = default);
    Task<int> PublishAsync(string patchesPath, string outputDir, string siteType, string siteName, bool whatIf, CancellationToken ct = default);
    Task EnsureGroupsAsync(string patchesPath, string outputDir, string cycle, bool whatIf, CancellationToken ct = default);
    Task<int> DeployAsync(string patchesPath, string outputDir, string stage, bool whatIf, CancellationToken ct = default);
    Task<ComplianceReport> ComplianceAsync(string patchesPath, string outputDir, string reportPath, CancellationToken ct = default);
}

public sealed class PatchPipeline : IPatchPipeline
{
    private readonly IUpdateCatalogClient _catalog;
    private readonly IPatchValidator _validator;
    private readonly IBigFixConsoleClient? _bigFix;
    private readonly IFixletWriter _writer;
    private readonly ILogger<PatchPipeline> _logger;

    public PatchPipeline(
        IUpdateCatalogClient catalog,
        IPatchValidator validator,
        IFixletWriter writer,
        ILogger<PatchPipeline> logger,
        IBigFixConsoleClient? bigFix = null)
    {
        _catalog = catalog;
        _validator = validator;
        _writer = writer;
        _logger = logger;
        _bigFix = bigFix;
    }

    private static string PatchesPath(string outputDir) => Path.Combine(outputDir, "patches.json");

    public async Task<List<PatchDefinition>> DiscoverAsync(DiscoverRequest request, string outputDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);
        var progress = new Progress<string>(m => Console.WriteLine($"  {m}"));
        var patches = await _catalog.DiscoverAsync(request, progress, ct);

        var path = PatchesPath(outputDir);
        await File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(patches, new JsonSerializerOptions { WriteIndented = true }), ct);
        _logger.LogInformation("Discovered {Count} patch(es) -> {Path}", patches.Count, path);
        Console.WriteLine($"Discovered {patches.Count} patch(es). Wrote {path}");
        return patches;
    }

    public async Task<PatchValidationResult> ValidateAsync(string patchesPath, bool download, CancellationToken ct = default)
    {
        var patches = await LoadPatchesAsync(patchesPath, ct);
        var progress = new Progress<string>(m => Console.WriteLine($"  {m}"));
        var result = await _validator.ValidateAllAsync(patches, download, progress, ct);

        foreach (var report in result.Reports)
        {
            Console.WriteLine($"\n{report.Kb}: {(report.Passed ? "PASS" : "FAIL")}");
            foreach (var check in report.Checks)
            {
                var mark = check.Passed ? "OK  " : check.Required ? "FAIL" : "WARN";
                Console.WriteLine($"  [{mark}] {check.Name}{(check.Message.Length > 0 ? " - " + check.Message : "")}");
            }
        }

        if (download)
            await SavePatchesAsync(patchesPath, patches, ct);

        Console.WriteLine($"\nValidation: {result.Reports.Count - result.FailedCount} passed, {result.FailedCount} failed.");
        return result;
    }

    public async Task<List<PatchDefinition>> GenerateAsync(string patchesPath, string outputDir, bool asTask, CancellationToken ct = default)
    {
        var patches = await LoadPatchesAsync(patchesPath, ct);
        Directory.CreateDirectory(outputDir);

        foreach (var patch in patches)
        {
            ct.ThrowIfCancellationRequested();
            var model = PatchContentGenerator.BuildFixletModel(patch, out var error);
            if (error.Length > 0 && model.Relevance.Count == 0)
            {
                Console.WriteLine($"  [FAIL] {patch.Kb}: {error}");
                continue;
            }
            if (error.Length > 0)
                Console.WriteLine($"  [WARN] {patch.Kb}: {error}");

            var xml = PatchContentGenerator.BuildBesXml(model, asTask);
            var fileName = FixletFactory.SanitizeFileName(model.Title) + ".bes";
            var filePath = Path.Combine(outputDir, fileName);
            var full = FixletWriter.ValidateOutputPath(filePath);
            await File.WriteAllTextAsync(full, xml, ct);
            patch.GeneratedBesPath = full;
            patch.ContentKind = asTask ? "task" : "fixlet";
            Console.WriteLine($"  [OK] {patch.Kb} -> {full}");
        }

        await SavePatchesAsync(patchesPath, patches, ct);
        Console.WriteLine($"Generated content for {patches.Count} patch(es) in {Path.GetFullPath(outputDir)}");
        return patches;
    }

    public async Task<int> PublishAsync(string patchesPath, string outputDir, string siteType, string siteName,
        bool whatIf, CancellationToken ct = default)
    {
        EnsureBigFix();
        var patches = await LoadPatchesAsync(patchesPath, ct);
        var statePath = PatchCycleState.DefaultPath(outputDir);
        var state = PatchCycleState.Load(statePath);
        state.Patches = patches;
        if (string.IsNullOrWhiteSpace(state.Cycle))
            state.Cycle = DeriveCycle(patches);

        var contentType = patches.FirstOrDefault()?.ContentKind == "task" ? "tasks" : "fixlets";
        var published = 0;
        var skipped = 0;
        var failed = 0;

        var existing = new List<RemoteContentItem>();
        if (!whatIf)
        {
            try
            {
                existing = await _bigFix!.ListContentAsync(contentType, siteType, siteName, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not list existing {Type} - dedupe limited", contentType);
            }
        }

        foreach (var patch in patches)
        {
            ct.ThrowIfCancellationRequested();
            var kb = PatchRelevance.NormalizeKb(patch.Kb);

            var already = existing.Any(c =>
                c.SourceId.Equals(patch.Kb, StringComparison.OrdinalIgnoreCase) ||
                c.Title.Contains(patch.Kb, StringComparison.OrdinalIgnoreCase) ||
                (c.SourceId.Length > 0 && kb.Equals(PatchRelevance.NormalizeKb(c.SourceId), StringComparison.OrdinalIgnoreCase)));

            var stateKey = contentType == "tasks"
                ? patch.Kb
                : patch.Kb;
            var publishedMap = contentType == "tasks" ? state.PublishedTaskIds : state.PublishedFixletIds;
            already |= publishedMap.ContainsKey(stateKey);

            if (already)
            {
                Console.WriteLine($"  [SKIP] {patch.Kb} already exists in BigFix");
                skipped++;
                continue;
            }

            string xml;
            if (!string.IsNullOrEmpty(patch.GeneratedBesPath) && File.Exists(patch.GeneratedBesPath))
                xml = await File.ReadAllTextAsync(patch.GeneratedBesPath, ct);
            else
            {
                var model = PatchContentGenerator.BuildFixletModel(patch, out _);
                xml = PatchContentGenerator.BuildBesXml(model, contentType == "tasks");
            }

            if (whatIf)
            {
                Console.WriteLine($"  [WHATIF] Would publish {patch.Kb} -> {contentType}/{siteType}/{siteName}");
                published++;
                continue;
            }

            try
            {
                var result = await _bigFix!.PublishContentXmlAsync(contentType, siteType, siteName, xml, ct);
                if (result.Success)
                {
                    publishedMap[stateKey] = result.FixletId?.ToString() ?? "ok";
                    patch.PublishedId = result.FixletId?.ToString();
                    Console.WriteLine($"  [OK] {patch.Kb} published{(result.FixletId.HasValue ? $" (ID {result.FixletId})" : "")}");
                    published++;
                }
                else
                {
                    Console.WriteLine($"  [FAIL] {patch.Kb}: {result.Message}");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [ERROR] {patch.Kb}: {ex.Message}");
                failed++;
            }
        }

        await SavePatchesAsync(patchesPath, patches, ct);
        state.Save(statePath);
        Console.WriteLine($"\nPublish: {published} published/planned, {skipped} skipped, {failed} failed.");
        return failed > 0 ? 1 : 0;
    }

    public async Task EnsureGroupsAsync(string patchesPath, string outputDir, string cycle, bool whatIf, CancellationToken ct = default)
    {
        EnsureBigFix();
        var patches = await LoadPatchesAsync(patchesPath, ct);
        var statePath = PatchCycleState.DefaultPath(outputDir);
        var state = PatchCycleState.Load(statePath);
        if (string.IsNullOrWhiteSpace(cycle))
            cycle = string.IsNullOrWhiteSpace(state.Cycle) ? DeriveCycle(patches) : state.Cycle;
        state.Cycle = cycle;

        var groupNames = BuildGroupNames(cycle, patches);
        foreach (var name in groupNames)
        {
            if (state.CreatedGroups.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  [SKIP] Group already tracked: {name}");
                continue;
            }

            if (whatIf)
            {
                Console.WriteLine($"  [WHATIF] Would ensure group: {name}");
                continue;
            }

            var xml = BuildAutomaticGroupXml(name, cycle, patches);
            try
            {
                var result = await _bigFix!.CreateAutomaticGroupAsync(xml, ct);
                if (result.Success)
                {
                    state.CreatedGroups.Add(name);
                    Console.WriteLine($"  [OK] Group ensured: {name}");
                }
                else
                {
                    Console.WriteLine($"  [WARN] Group '{name}': {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [ERROR] Group '{name}': {ex.Message}");
            }
        }

        state.Save(statePath);
    }

    public async Task<int> DeployAsync(string patchesPath, string outputDir, string stage, bool whatIf, CancellationToken ct = default)
    {
        EnsureBigFix();
        if (!PatchStages.IsValid(stage))
        {
            Console.WriteLine($"Unknown stage '{stage}'. Use: {string.Join(", ", PatchStages.All)}");
            return 2;
        }

        var patches = await LoadPatchesAsync(patchesPath, ct);
        var statePath = PatchCycleState.DefaultPath(outputDir);
        var state = PatchCycleState.Load(statePath);
        if (string.IsNullOrWhiteSpace(state.Cycle))
            state.Cycle = DeriveCycle(patches);

        var groupName = $"Microsoft Patches/{state.Cycle}/{PatchStages.DisplayName(stage)}";
        var failed = 0;

        foreach (var patch in patches)
        {
            ct.ThrowIfCancellationRequested();
            var actionKey = $"{patch.Kb}:{stage}";
            if (state.DeploymentActionIds.ContainsKey(actionKey))
            {
                Console.WriteLine($"  [SKIP] {patch.Kb} already deployed to {stage} (action {state.DeploymentActionIds[actionKey]})");
                continue;
            }

            var model = PatchContentGenerator.BuildFixletModel(patch, out _);
            var relevance = new List<string>(model.Relevance)
            {
                $"member of computer group \"{groupName}\""
            };

            var deployModel = new FixletModel
            {
                Title = $"Deploy {model.Title} [{PatchStages.DisplayName(stage)}]",
                Category = model.Category,
                Source = model.Source,
                SourceId = model.SourceId,
                Description = model.Description,
                Relevance = relevance,
                ActionDescription = model.ActionDescription,
                ActionScript = model.ActionScript,
                SuccessCriteria = model.SuccessCriteria,
                Sha1 = model.Sha1,
                Sha256 = model.Sha256,
                FileSizeBytes = model.FileSizeBytes
            };

            var writer = new FixletWriter(NullLogger<FixletWriter>.Instance);
            var actionXml = writer.GenerateXml(deployModel);

            if (whatIf)
            {
                Console.WriteLine($"  [WHATIF] Would start action for {patch.Kb} -> {groupName}");
                continue;
            }

            try
            {
                var result = await _bigFix!.StartActionAsync(actionXml, ct);
                if (result.Success && result.FixletId.HasValue)
                {
                    state.DeploymentActionIds[actionKey] = result.FixletId.Value;
                    Console.WriteLine($"  [OK] {patch.Kb} -> {stage}: action {result.FixletId.Value}");
                }
                else if (result.Success)
                {
                    Console.WriteLine($"  [OK] {patch.Kb} -> {stage}: action started");
                }
                else
                {
                    Console.WriteLine($"  [FAIL] {patch.Kb} -> {stage}: {result.Message}");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [ERROR] {patch.Kb} -> {stage}: {ex.Message}");
                failed++;
            }
        }

        state.Save(statePath);
        return failed > 0 ? 1 : 0;
    }

    public async Task<ComplianceReport> ComplianceAsync(string patchesPath, string outputDir, string reportPath, CancellationToken ct = default)
    {
        EnsureBigFix();
        var patches = await LoadPatchesAsync(patchesPath, ct);
        var state = PatchCycleState.Load(PatchCycleState.DefaultPath(outputDir));
        var report = new ComplianceReport
        {
            Cycle = string.IsNullOrWhiteSpace(state.Cycle) ? DeriveCycle(patches) : state.Cycle
        };

        foreach (var patch in patches)
        {
            ct.ThrowIfCancellationRequested();
            var row = new ComplianceRow
            {
                Kb = patch.Kb,
                Title = patch.Title,
                Product = patch.Product
            };

            foreach (var stage in PatchStages.All)
            {
                var key = $"{patch.Kb}:{stage}";
                if (!state.DeploymentActionIds.TryGetValue(key, out var actionId))
                    continue;

                row.Stage = stage;
                row.ActionId = actionId.ToString();
                var status = await _bigFix!.GetActionStatusAsync(actionId, ct);
                if (status is not null)
                {
                    row.RelevantComputers = status.RelevantComputers;
                    row.Taken = status.Taken;
                    row.Failed = status.Failed;
                    row.Pending = status.Pending;
                    row.Notes = $"Action {actionId} ({stage})";
                }
            }

            if (!string.IsNullOrWhiteSpace(patch.Kb))
            {
                try
                {
                    var relevance = PatchRelevance.BuildEstateInstalledRelevance(patch.Kb);
                    var body = await _bigFix!.EvaluateRelevanceAsync(relevance, ct);
                    if (!string.IsNullOrWhiteSpace(body))
                        row.InstalledEstimate = CountNamesInRelevanceResult(body);
                }
                catch (Exception ex)
                {
                    row.Notes = string.IsNullOrWhiteSpace(row.Notes)
                        ? ex.Message
                        : row.Notes + "; " + ex.Message;
                }
            }

            report.Rows.Add(row);
            Console.WriteLine(
                $"  {row.Kb,-10} stage={row.Stage,-6} taken={row.Taken?.ToString() ?? "-"} " +
                $"failed={row.Failed?.ToString() ?? "-"} installed≈{row.InstalledEstimate?.ToString() ?? "-"}");
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
        Console.WriteLine($"\nCompliance report: {reportPath}");
        return report;
    }

    private void EnsureBigFix()
    {
        if (_bigFix is null)
            throw new InvalidOperationException(
                "BigFix is not configured. Add a 'BigFix' section to appsettings.json (BaseUrl, Username, Password).");
    }

    private async Task<List<PatchDefinition>> LoadPatchesAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Patch file not found: {path}");
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<List<PatchDefinition>>(json) ?? new List<PatchDefinition>();
    }

    private static async Task SavePatchesAsync(string path, List<PatchDefinition> patches, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(patches, new JsonSerializerOptions { WriteIndented = true }), ct);
    }

    private static string DeriveCycle(List<PatchDefinition> patches)
    {
        var date = patches.Select(p => p.ReleaseDate).Where(d => d.HasValue).Max();
        var anchor = date?.Date ?? DateTime.Today;
        return $"{anchor:yyyy-MM}";
    }

    private static List<string> BuildGroupNames(string cycle, List<PatchDefinition> patches)
    {
        var groups = new List<string>
        {
            $"Microsoft Patches",
            $"Microsoft Patches/{cycle}"
        };

        foreach (var stage in PatchStages.All)
            groups.Add($"Microsoft Patches/{cycle}/{PatchStages.DisplayName(stage)}");

        var products = patches
            .Select(p =>
            {
                PatchRelevance.TryGetProfile(p.Product, out var profile);
                return profile?.DisplayName ?? p.Product;
            })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var product in products)
            groups.Add($"Microsoft Patches/{cycle}/{product}");

        return groups;
    }

    private static string BuildAutomaticGroupXml(string name, string cycle, List<PatchDefinition> patches)
    {
        string relevance;
        if (name.EndsWith("/Pilot", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("/Wave ", StringComparison.OrdinalIgnoreCase))
        {
            relevance = "true";
        }
        else
        {
            var productLeaf = name.Split('/').Last();
            if (PatchRelevance.TryGetProfile(productLeaf, out var profile) && profile is not null)
            {
                relevance = $"(name of operating system = \"{profile.OsName}\")\n" +
                            $"(version of operating system as string as version >= \"{profile.MinVersion}\" as version)\n" +
                            $"(version of operating system as string as version < \"{profile.MaxVersionExclusive}\" as version)";
            }
            else
            {
                relevance = "true";
            }
        }

        var title = System.Security.SecurityElement.Escape(name);
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <BES>
              <AutomaticGroup>
                <Title>{title}</Title>
                <Relevance>{System.Security.SecurityElement.Escape(relevance)}</Relevance>
              </AutomaticGroup>
            </BES>
            """;
    }

    private static long? CountNamesInRelevanceResult(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                return doc.RootElement.GetArrayLength();
            if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                var s = doc.RootElement.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(s))
                    return 0;
                return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            }
        }
        catch
        {
        }
        return null;
    }
}
