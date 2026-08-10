using VPX_Inspector.UI;
using VPX_Inspector.Vpx;
using VPX_Inspector.Vpx.Rules;

// No arguments -> launch the GUI (Windows only). With arguments -> console scan.
if (args.Length == 0)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.WriteLine("Usage: VPX Inspector <path-to-vpx-file-or-folder> [rules.json]");
        return 1;
    }

    return AppUi.Run();
}

return RunConsole(args);

static int RunConsole(string[] args)
{
    string inputPath = args[0];
    string rulesPath = args.Length > 1
        ? args[1]
        : Path.Combine(AppContext.BaseDirectory, "rules.json");

    if (!File.Exists(rulesPath))
    {
        Console.WriteLine($"Rules file not found at '{rulesPath}'.");
        return 1;
    }

    RuleEngine engine = RuleEngine.LoadFromFile(rulesPath);
    var service = new TableScanService(engine);

    IReadOnlyList<string> vpxFiles = TableScanService.ResolveVpxFiles(
        inputPath, out string? error, engine.Settings.ExcludePatterns);
    if (error is not null)
    {
        Console.WriteLine(error);
        return 1;
    }

    if (vpxFiles.Count == 0)
    {
        Console.WriteLine($"No .vpx files found at '{inputPath}'.");
        return 1;
    }

    Console.WriteLine($"Rules:   {rulesPath}");
    Console.WriteLine($"Scanning {vpxFiles.Count} table(s) from: {inputPath}");
    if (engine.Settings.ExcludePatterns.Count > 0)
    {
        Console.WriteLine($"Excluding: {string.Join(", ", engine.Settings.ExcludePatterns)}");
    }
    if (engine.Settings.MaxRunTimeSeconds > 0)
    {
        Console.WriteLine($"Max run time: {engine.Settings.MaxRunTimeSeconds}s");
    }
    Console.WriteLine();

    // Skip the per-table scan entirely when no deep-analysis rules are enabled.
    bool anyRuleEnabled = engine.Rules.Any(r => r.Enabled);
    IReadOnlyList<TableResult> results;
    if (anyRuleEnabled)
    {
        results = service.ScanFiles(
            vpxFiles,
            onResult: (result, _, _) =>
            {
                Console.Write(ReportFormatter.FormatTableDetail(result));
                Console.WriteLine();
            });

        Console.WriteLine(ReportFormatter.FormatSummary(results));
    }
    else
    {
        results = Array.Empty<TableResult>();
        Console.WriteLine("No deep-analysis rules enabled; skipping per-table scan.");
    }

    // Configuration (collection-scope) checks, e.g. PinUP game match.
    var checkContext = new VPX_Inspector.Vpx.Checks.ConfigurationCheckContext
    {
        InputPath = inputPath,
        ExcludePatterns = engine.Settings.ExcludePatterns,
        IsFullScan = true,
    };
    var checkResults = VPX_Inspector.Vpx.Checks.ConfigurationCheckRunner.Run(
        engine.Settings, checkContext);
    foreach (var checkResult in checkResults)
    {
        Console.Write(ReportFormatter.FormatConfigurationCheck(checkResult));
    }

    return 0;
}
