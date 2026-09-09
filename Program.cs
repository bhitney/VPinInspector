using System.Runtime.InteropServices;
using VPin.Inspector.Core;
using VPin.Inspector.Core.Reporting;
using VPin.Inspector.Platforms.Vpx;
using VPin.Inspector.Platforms.Vpx.Reporting;
using VPin.Inspector.UI;
using VPin.Inspector.Vpx.Rules;

// Help flag -> print usage and exit, regardless of other args.
if (args.Any(a => a is "-h" or "--help" or "-help" or "/?"))
{
    PrintUsage();
    return 0;
}

// No arguments -> launch the GUI (Windows only). With arguments -> console scan.
if (args.Length == 0)
{
    if (!OperatingSystem.IsWindows())
    {
        PrintUsage();
        return 1;
    }

    // The app is built as a console-subsystem Exe so console mode can write to a
    // parent terminal. Detach that console before showing the form so the GUI
    // doesn't leave a stray console window behind.
    NativeMethods.FreeConsole();
    return AppUi.Run();
}

return RunConsole(args);

static void PrintUsage()
{
    Console.WriteLine("VPin Inspector - inspect Visual Pinball tables and PinUP Popper configuration.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  VPin Inspector                         Launch the GUI (Windows only).");
    Console.WriteLine("  VPin Inspector <path> [rules.json]     Scan a .vpx file or folder from the console.");
    Console.WriteLine("  VPin Inspector -h | --help             Show this help.");
    Console.WriteLine();
    Console.WriteLine("Arguments:");
    Console.WriteLine("  <path>        Path to a .vpx file or a folder containing .vpx files.");
    Console.WriteLine("  [rules.json]  Optional path to a rules file. Defaults to rules.json next to the exe.");
    Console.WriteLine();
    Console.WriteLine("Console scans run the deep-analysis rules plus the configuration checks");
    Console.WriteLine("(e.g. pinup-game-match, media-match) defined in rules.json.");
}

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
    InspectionRegistry registry = VpxRegistryFactory.Build(engine);
    var service = new InspectionService(registry);

    var options = new ScanOptions
    {
        ExcludePatterns = engine.Settings.ExcludePatterns,
        MaxRunTimeSeconds = engine.Settings.MaxRunTimeSeconds,
        MaxDegreeOfParallelism = engine.Settings.MaxDegreeOfParallelism,
        HiddenFileNames = new HiddenTablesStore().Load(),
    };

    IReadOnlyList<string> vpxFiles = service.ResolveFiles(inputPath, options);
    if (vpxFiles.Count == 0)
    {
        Console.WriteLine($"No tables found at '{inputPath}'.");
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

    ScanReport report = service.Scan(
        inputPath,
        options,
        onTable: (table, _, _) =>
        {
            Console.Write(ReportRenderer.FormatTableDetail(table));
            Console.WriteLine();
        });

    Console.WriteLine(ReportRenderer.FormatSummary(report));

    return 0;
}

/// <summary>Native interop for detaching the console window in GUI mode.</summary>
internal static class NativeMethods
{
    /// <summary>
    /// Detaches the calling process from its console. Used before showing the GUI
    /// so the console-subsystem exe doesn't leave a stray console window open.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FreeConsole();
}
