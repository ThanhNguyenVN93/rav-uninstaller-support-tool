using RavUninstallerTool.Models;
using RavUninstallerTool.Services;

namespace RavUninstallerTool;

public enum StartupAction { None, Scan, CleanRemnants, AutoSafeMode, SafeModeClean, PostVerify }

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        UpdateService.CleanupOldExecutable();
        ApplicationConfiguration.Initialize();

        if (Environment.OSVersion.Version.Major < 10)
        {
            MessageBox.Show(LocalizationService.T("os_unsupported") + "\n\n" + Environment.OSVersion,
                LocalizationService.T("app_title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        var action = StartupAction.None;
        foreach (var a in args)
        {
            switch (a.ToLowerInvariant())
            {
                case "--scan": action = StartupAction.Scan; break;
                case "--clean-remnants": action = StartupAction.CleanRemnants; break;
                case "--auto-safemode": action = StartupAction.AutoSafeMode; break;
                case "--safemode-clean": action = StartupAction.SafeModeClean; break;
                case "--post-verify": action = StartupAction.PostVerify; break;
                default:
                    MessageBox.Show(LocalizationService.T("cli_unknown", a), LocalizationService.T("app_title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 2;
            }
        }

        // Single instance (the logon task and RunOnce fallback may both fire).
        using var mutex = new Mutex(true, @"Global\RavUninstallerTool.SingleInstance", out bool created);
        if (!created) return 0;

        // Automatic phases must only run when the workflow state matches; otherwise a stray trigger would do nothing.
        if (action is StartupAction.SafeModeClean or StartupAction.PostVerify)
        {
            var state = CleanerService.LoadState();
            var expected = action == StartupAction.SafeModeClean ? AppPhase.SafeModeCleaning : AppPhase.PostCleanVerify;
            if (state == null || state.Phase != expected) return 0;
        }

        // When launched normally without CLI flags, check first if RAV / ReasonLabs is actually installed.
        ScanResult? initialScan = null;
        if (action == StartupAction.None)
        {
            initialScan = SystemScannerService.Scan();
            if (initialScan.Items.Count == 0)
            {
                MessageBox.Show(LocalizationService.T("not_installed"), LocalizationService.T("app_title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
        }

        try
        {
            Application.Run(new MainForm(action, initialScan));
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, LocalizationService.T("error_title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
