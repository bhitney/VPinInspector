using System.Runtime.Versioning;
using System.Windows.Forms;

namespace VPX_Inspector.UI;

/// <summary>
/// Entry point for the graphical interface. Kept separate so the console path
/// has no dependency on WinForms initialization.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AppUi
{
    public static int Run()
    {
        // The Vista-style FolderBrowserDialog (COM IFileDialog) requires the UI
        // thread to be in a single-threaded apartment (STA). Top-level statements
        // generate a Main without [STAThread], so the process starts as MTA. Run
        // the WinForms message loop on a dedicated STA thread to guarantee STA
        // regardless of how the entry point was generated.
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            RunMessageLoop();
            return 0;
        }

        var uiThread = new Thread(RunMessageLoop)
        {
            Name = "VPX Inspector UI",
            IsBackground = false,
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        uiThread.Join();
        return 0;
    }

    private static void RunMessageLoop()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
