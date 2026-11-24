using System.Diagnostics;
using System.Management;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using RavUninstallerTool.Models;

namespace RavUninstallerTool.Services;

/// <summary>Runs the embedded RAV.ps1 cleaner and orchestrates the Safe Mode workflow.</summary>
public static class CleanerService
{
    public const string TaskName = "RAVAutoCleanTask";
    public const string WorkDir = @"C:\ProgramData\RAVCleaner";
    public static readonly string ScriptPath = Path.Combine(WorkDir, "RAV.ps1");
    public static readonly string StatePath = Path.Combine(WorkDir, "state.json");
    public static readonly string CopiedExePath = Path.Combine(WorkDir, "RAVCleaner.exe");
    public static readonly string DefaultLogPath = Path.Combine(WorkDir, "RAVCleaner.log");

    private const string RunOnceKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string RunOnceValue = "*RAVAutoClean"; // '*' => also executed in Safe Mode

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // ------------------------------------------------------------ script / state

    public static string ExtractScript()
    {
        Directory.CreateDirectory(WorkDir);
        var asm = Assembly.GetExecutingAssembly();
        string? res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("RAV.ps1", StringComparison.OrdinalIgnoreCase))
                      ?? throw new FileNotFoundException("Embedded RAV.ps1 not found.");
        using var s = asm.GetManifestResourceStream(res)!;
        using var f = File.Create(ScriptPath);
        s.CopyTo(f);
        return ScriptPath;
    }

    public static WorkflowState? LoadState()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<WorkflowState>(File.ReadAllText(StatePath), JsonOpts)
                : null;
        }
        catch { return null; }
    }

    public static void SaveState(WorkflowState state)
    {
        Directory.CreateDirectory(WorkDir);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOpts));
    }

    // ------------------------------------------------------------ restore point

    public static void CreateSilentRestorePoint(Action<string> log)
    {
        try
        {
            // Enable System Restore on C:\ (no-op when already enabled).
            try
            {
                using var enable = new ManagementClass(@"\\.\root\default:SystemRestore");
                enable.InvokeMethod("Enable", new object[] { @"C:\", true });
            }
            catch (Exception ex) { log($"Enable System Restore: {ex.Message}"); }

            using var mc = new ManagementClass(@"\\.\root\default:SystemRestore");
            var result = mc.InvokeMethod("CreateRestorePoint", new object[] { "Before RAV Removal", 12, 100 });
            int code = result is null ? 0 : Convert.ToInt32(result);
            if (code == 0) log(LocalizationService.T("restore_ok"));
            else log(LocalizationService.T("restore_warn", $"WMI code {code}"));
        }
        catch (Exception ex)
        {
            log(LocalizationService.T("restore_warn", ex.Message));
        }
    }

    // ------------------------------------------------------------ process helpers

    private static async Task<int> RunAsync(string file, IEnumerable<string> args, Action<string>? onLine = null, bool hidden = true)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) onLine?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) onLine?.Invoke(e.Data); };
        p.Start();
        p.StandardInput.Close();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync().ConfigureAwait(false);
        return p.ExitCode;
    }

    private static Task<int> RunQuietAsync(string file, params string[] args) => RunAsync(file, args);

    private static void Reboot(Action<string> log)
    {
        log("shutdown /r /t 3");
        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 3") { UseShellExecute = false, CreateNoWindow = true });
    }

    // ------------------------------------------------------------ PowerShell cleaner

    public static async Task<int> RunPowerShellCleanerAsync(bool auto, string logFile, Action<int, string> onProgress)
    {
        ExtractScript();
        Directory.CreateDirectory(Path.GetDirectoryName(logFile) ?? WorkDir);

        var args = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ScriptPath };
        if (auto) args.Add("-Auto");
        args.Add("-LogFile");
        args.Add(logFile);

        int lines = 0;
        onProgress(5, "RAV.ps1");
        int exit = await RunAsync("powershell.exe", args, line =>
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lines++;
            int pct = Math.Min(95, 5 + lines * 2);
            onProgress(pct, line.Trim());
        }).ConfigureAwait(false);

        onProgress(100, $"RAV.ps1 exit code {exit}");
        return exit;
    }

    // ------------------------------------------------------------ scheduled task / RunOnce

    private static async Task CreateLogonTaskAsync(string argument, Action<string> log, bool useSchtasks, bool safeModeRunOnce)
    {
        if (useSchtasks)
        {
            try
            {
                int rc = await RunQuietAsync("schtasks.exe", "/create", "/tn", TaskName,
                    "/tr", $"\"{CopiedExePath}\" {argument}", "/sc", "onlogon", "/rl", "highest", "/f").ConfigureAwait(false);
                log($"schtasks /create {argument} => {rc}");
            }
            catch (Exception ex) { log($"schtasks: {ex.Message}"); }
        }

        try
        {
            // RunOnce (CreateProcess) fails with ERROR_ELEVATION_REQUIRED on requireAdministrator manifests;
            // launching via powershell.exe Start-Process -Verb RunAs ensures elevation in Safe Mode.
            string cmd = $"powershell.exe -NoProfile -WindowStyle Hidden -Command \"Start-Process -FilePath '{CopiedExePath}' -ArgumentList '{argument}' -Verb RunAs\"";
            string valName = safeModeRunOnce ? RunOnceValue : "RAVAutoCleanPost";
            using var k = Registry.LocalMachine.CreateSubKey(RunOnceKey);
            k.SetValue(valName, cmd);
            log($"RunOnce ({valName}) => {argument}");
        }
        catch (Exception ex) { log($"RunOnce: {ex.Message}"); }
    }

    private static void RemoveRunOnce()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(RunOnceKey, true);
            k?.DeleteValue(RunOnceValue, false);
            k?.DeleteValue("RAVAutoCleanPost", false);
        }
        catch { /* ignore */ }
    }

    // ------------------------------------------------------------ Safe Mode workflow

    public static async Task<bool> PrepareSafeModeAndReboot(Action<int, string> onProgress, Action<string> log, bool createRestorePoint = true)
    {
        bool safebootSet = false;
        try
        {
            onProgress(10, LocalizationService.T("step_prepare_safe"));
            Directory.CreateDirectory(WorkDir);
            string src = Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath unavailable.");
            if (!string.Equals(Path.GetFullPath(src), CopiedExePath, StringComparison.OrdinalIgnoreCase))
                File.Copy(src, CopiedExePath, true);
            ExtractScript();
            log($"Copied to {CopiedExePath}");

            if (createRestorePoint)
            {
                onProgress(25, LocalizationService.T("step_restore"));
                await Task.Run(() => CreateSilentRestorePoint(log)).ConfigureAwait(false);
            }

            onProgress(45, "state.json");
            SaveState(new WorkflowState
            {
                Phase = AppPhase.SafeModeCleaning,
                CreatedAt = DateTime.Now.ToString("o"),
                OriginalExePath = src,
                LogFilePath = DefaultLogPath
            });

            onProgress(60, "RunOnce (*RAVAutoClean)");
            // Skip schtasks in Normal Mode to avoid RAV behavioral shield blocking schtasks.exe
            await CreateLogonTaskAsync("--safemode-clean", log, useSchtasks: false, safeModeRunOnce: true).ConfigureAwait(false);

            onProgress(80, "bcdedit safeboot minimal");
            int rc = await RunQuietAsync("bcdedit.exe", "/set", "{current}", "safeboot", "minimal").ConfigureAwait(false);
            if (rc != 0) throw new InvalidOperationException($"bcdedit failed (exit {rc}).");
            safebootSet = true;

            onProgress(95, LocalizationService.T("step_reboot"));
            Reboot(log);
            onProgress(100, LocalizationService.T("step_reboot"));
            return true;
        }
        catch (Exception ex)
        {
            log(LocalizationService.T("safe_prepare_failed", ex.Message));
            // Roll back so the machine never boots into Safe Mode by accident.
            if (safebootSet) await RunQuietAsync("bcdedit.exe", "/deletevalue", "{current}", "safeboot").ConfigureAwait(false);
            await RunQuietAsync("schtasks.exe", "/delete", "/tn", TaskName, "/f").ConfigureAwait(false);
            RemoveRunOnce();
            try { File.Delete(StatePath); } catch { /* ignore */ }
            return false;
        }
    }

    public static async Task ExecuteSafeModeCleanupAsync(Action<int, string> onProgress, Action<string> log)
    {
        // FIRST: guarantee we never loop back into Safe Mode.
        try
        {
            int rc = await RunQuietAsync("bcdedit.exe", "/deletevalue", "{current}", "safeboot").ConfigureAwait(false);
            log($"bcdedit /deletevalue {{current}} safeboot => {rc}");
        }
        catch (Exception ex) { log($"bcdedit: {ex.Message}"); }
        RemoveRunOnce();

        var state = LoadState() ?? new WorkflowState { CreatedAt = DateTime.Now.ToString("o") };
        string logFile = state.LogFilePath ?? DefaultLogPath;

        try
        {
            onProgress(5, LocalizationService.T("step_safe_clean"));
            int exit = await RunPowerShellCleanerAsync(true, logFile, (p, m) =>
            {
                onProgress(Math.Min(85, p * 85 / 100), m);
                log(m);
            }).ConfigureAwait(false);
            log($"RAV.ps1 exit code {exit}");
        }
        catch (Exception ex)
        {
            log(LocalizationService.T("clean_failed", ex.Message));
        }

        try
        {
            onProgress(88, "state.json");
            state.Phase = AppPhase.PostCleanVerify;
            SaveState(state);

            onProgress(92, TaskName);
            await CreateLogonTaskAsync("--post-verify", log, useSchtasks: true, safeModeRunOnce: false).ConfigureAwait(false);
        }
        catch (Exception ex) { log(ex.Message); }

        onProgress(100, LocalizationService.T("step_reboot"));
        await Task.Delay(1500).ConfigureAwait(false);
        Reboot(log);
    }

    public static async Task CompletePostCleanAndCleanup(Action<string> log)
    {
        try
        {
            int rc = await RunQuietAsync("schtasks.exe", "/delete", "/tn", TaskName, "/f").ConfigureAwait(false);
            log($"schtasks /delete => {rc}");
        }
        catch (Exception ex) { log($"schtasks: {ex.Message}"); }

        RemoveRunOnce();
        SafeBootGuard(log);

        try
        {
            // Keep a copy of the log for the user before the work directory is removed.
            string log0 = LoadState()?.LogFilePath ?? DefaultLogPath;
            if (File.Exists(log0))
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (Directory.Exists(desktop)) File.Copy(log0, Path.Combine(desktop, "RAVCleaner.log"), true);
            }
        }
        catch { /* best effort */ }

        try { if (File.Exists(StatePath)) File.Delete(StatePath); }
        catch (Exception ex) { log($"state.json: {ex.Message}"); }

        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{WorkDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex) { log($"cleanup: {ex.Message}"); }
    }

    /// <summary>Ensures no stale safeboot flag remains (harmless if absent).</summary>
    private static void SafeBootGuard(Action<string> log)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("bcdedit.exe", "/deletevalue {current} safeboot")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
            p?.WaitForExit(5000);
        }
        catch (Exception ex) { log($"bcdedit guard: {ex.Message}"); }
    }
}
