using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RavUninstallerTool.Models;

namespace RavUninstallerTool.Services;

/// <summary>Scans the machine for RAV / ReasonLabs leftovers. Never throws; all failures are swallowed per-source.</summary>
public static class SystemScannerService
{
    private static readonly Regex NameRx = new(
        @"ReasonLabs|Reason\s*Cyber|(^|[^A-Za-z])RAV([^A-Za-z]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ServiceRx = new(
        @"ReasonLabs|(^|[^A-Za-z])RAV([^A-Za-z]|$)|\\rs[A-Za-z0-9_]*\.(sys|exe)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ServiceNameRx = new(@"^rs[A-Z][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex DriverNameRx = new(@"rsCamFilter|rsElam|rsRTP", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ScanResult Scan(Action<string>? log = null)
    {
        var items = new List<ScanItem>();
        string? uninstallExe = null, uninstallArgs = null;

        var os = Environment.OSVersion.Version;
        bool win10 = os.Major >= 10;
        bool safe = SafeGet(() => SystemInformation.BootMode != BootMode.Normal, false);
        string osDesc = SafeGet(GetOsDescription, RuntimeInformation.OSDescription);
        string arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";

        void Run(string name, Action a)
        {
            try { a(); }
            catch (Exception ex) { log?.Invoke($"[scan:{name}] {ex.Message}"); }
        }

        Run("folders", () => ScanFolders(items));
        Run("appreg", () => ScanAppRegistry(items));
        Run("uninstall", () => (uninstallExe, uninstallArgs) = ScanUninstall(items));
        Run("run", () => ScanRunKeys(items));
        Run("services", () => ScanServices(items));
        Run("drivers", () => ScanDrivers(items));
        Run("tasks", () => ScanTasks(items));
        Run("shortcuts", () => ScanShortcuts(items));
        Run("policy", () => ScanBrowserPolicies(items));

        string defender = SafeGet(GetDefenderStatus, "Unknown");
        bool active = items.Any(i => i.IsCritical);

        return new ScanResult(items, uninstallExe, uninstallArgs, active, win10, osDesc, arch, defender, safe);
    }

    // ---------------------------------------------------------------- helpers

    private static T SafeGet<T>(Func<T> f, T fallback)
    {
        try { return f(); } catch { return fallback; }
    }

    private static string GetOsDescription()
    {
        var v = Environment.OSVersion.Version;
        string name = v.Major >= 10 && v.Build >= 22000 ? "Windows 11" : v.Major >= 10 ? "Windows 10" : "Windows " + v.Major + "." + v.Minor;
        return $"{name} (build {v.Build})";
    }

    private static IEnumerable<RegistryView> Views => new[] { RegistryView.Registry64, RegistryView.Registry32 };

    private static string ViewTag(RegistryView v) => v == RegistryView.Registry32 ? "32-bit view" : "64-bit view";

    private static string HiveName(RegistryHive h) => h switch
    {
        RegistryHive.LocalMachine => "HKEY_LOCAL_MACHINE",
        RegistryHive.CurrentUser => "HKEY_CURRENT_USER",
        _ => h.ToString()
    };

    /// <summary>Real registry path as seen in regedit for a given view (32-bit view lives under WOW6432Node).</summary>
    private static string Shown(RegistryView view, string sub) =>
        view == RegistryView.Registry32 && sub.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase)
            ? @"SOFTWARE\WOW6432Node\" + sub[@"SOFTWARE\".Length..]
            : sub;

    private static RegistryKey? TryOpen(RegistryHive hive, RegistryView view, string sub)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            return baseKey.OpenSubKey(sub, false);
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------- folders

    private static void ScanFolders(List<ScanItem> items)
    {
        var roots = new List<string?>
        {
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Path.GetTempPath()
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            ScanDir(root!, 1, 2, items, seen);
    }

    private static void ScanDir(string dir, int depth, int maxDepth, List<ScanItem> items, HashSet<string> seen)
    {
        IEnumerable<string> subs;
        try { subs = Directory.EnumerateDirectories(dir).ToList(); }
        catch { return; }

        foreach (var sub in subs)
        {
            string name = Path.GetFileName(sub);
            if (NameRx.IsMatch(name))
            {
                if (seen.Add(sub))
                    items.Add(new ScanItem(ScanCategory.Folders, sub, SafeGet(() => DescribeDir(sub), ""), "Present", false));
            }
            else if (depth < maxDepth)
            {
                ScanDir(sub, depth + 1, maxDepth, items, seen);
            }
        }
    }

    private static string DescribeDir(string path)
    {
        int files = Directory.EnumerateFiles(path, "*", new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Take(1000).Count();
        return $"{files} file(s)";
    }

    // ---------------------------------------------------------------- registry

    private static void ScanAppRegistry(List<ScanItem> items)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in Views)
        {
            if (hive == RegistryHive.CurrentUser && view == RegistryView.Registry32) continue; // HKCU is shared
            using var key = TryOpen(hive, view, @"SOFTWARE\ReasonLabs");
            if (key == null) continue;
            items.Add(new ScanItem(ScanCategory.Registry, $@"{HiveName(hive)}\{Shown(view, @"SOFTWARE\ReasonLabs")}",
                $"{key.SubKeyCount} subkey(s), {ViewTag(view)}", "Present", false));
        }
    }

    private static readonly string[] UninstallRoots =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    private static (string? Exe, string? Args) ScanUninstall(List<ScanItem> items)
    {
        string? exe = null, args = null;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in Views)
        {
            if (hive == RegistryHive.CurrentUser && view == RegistryView.Registry32) continue;
            using var root = TryOpen(hive, view, UninstallRoots[0]);
            if (root == null) continue;

            foreach (var name in SafeGet(() => root.GetSubKeyNames(), Array.Empty<string>()))
            {
                try
                {
                    using var k = root.OpenSubKey(name);
                    var display = k?.GetValue("DisplayName") as string;
                    if (k == null || string.IsNullOrEmpty(display) || !NameRx.IsMatch(display)) continue;

                    var us = (k.GetValue("UninstallString") as string) ?? "";
                    items.Add(new ScanItem(ScanCategory.Registry,
                        $@"{HiveName(hive)}\{Shown(view, UninstallRoots[0])}\{name}", $"{display} | {us}", "Installed", false));

                    if (exe == null && !string.IsNullOrWhiteSpace(us) && TrySplitCommand(us, out var e, out var a) && File.Exists(e))
                    {
                        exe = e; args = a;
                    }
                }
                catch { /* ignore broken key */ }
            }
        }
        return (exe, args);
    }

    private static bool TrySplitCommand(string cmd, out string exe, out string args)
    {
        exe = ""; args = "";
        cmd = cmd.Trim();
        if (cmd.Length == 0) return false;
        if (cmd[0] == '"')
        {
            int end = cmd.IndexOf('"', 1);
            if (end < 0) return false;
            exe = cmd[1..end];
            args = cmd[(end + 1)..].Trim();
            return true;
        }
        int idx = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;
        exe = cmd[..(idx + 4)];
        args = cmd[(idx + 4)..].Trim();
        return true;
    }

    private static void ScanRunKeys(List<ScanItem> items)
    {
        string[] subs =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
        };
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in Views)
        {
            // The 64-bit view with explicit WOW6432Node paths already covers the 32-bit view.
            if (view == RegistryView.Registry32) continue;
            foreach (var sub in subs)
            {
                using var k = TryOpen(hive, view, sub);
                if (k == null) continue;
                foreach (var vn in SafeGet(() => k.GetValueNames(), Array.Empty<string>()))
                {
                    var data = k.GetValue(vn)?.ToString() ?? "";
                    if (NameRx.IsMatch(vn) || NameRx.IsMatch(data) || ServiceRx.IsMatch(data))
                        items.Add(new ScanItem(ScanCategory.Registry, $@"{HiveName(hive)}\{sub}\{vn}", data, "Startup", false));
                }
            }
        }
    }

    // ---------------------------------------------------------------- services

    private static void ScanServices(List<ScanItem> items)
    {
        var running = new Dictionary<string, ServiceControllerStatus>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var sc in ServiceController.GetServices())
            {
                using (sc) { running[sc.ServiceName] = SafeGet(() => sc.Status, ServiceControllerStatus.Stopped); }
            }
        }
        catch { /* SCM unavailable */ }

        using var svcRoot = TryOpen(RegistryHive.LocalMachine, RegistryView.Registry64, @"SYSTEM\CurrentControlSet\Services");
        if (svcRoot == null) return;

        foreach (var name in SafeGet(() => svcRoot.GetSubKeyNames(), Array.Empty<string>()))
        {
            try
            {
                using var k = svcRoot.OpenSubKey(name);
                if (k == null) continue;
                var image = k.GetValue("ImagePath")?.ToString() ?? "";
                var display = k.GetValue("DisplayName")?.ToString() ?? "";
                int type = k.GetValue("Type") is int t ? t : 0;
                bool isDriver = (type & 0x3) != 0; // kernel / filesystem driver -> shown on Drivers tab

                bool match = ServiceNameRx.IsMatch(name) || ServiceRx.IsMatch(image) || NameRx.IsMatch(display);
                if (!match || isDriver) continue;

                bool isRunning = running.TryGetValue(name, out var st) && st == ServiceControllerStatus.Running;
                items.Add(new ScanItem(ScanCategory.Services, name,
                    $"{display} | {image}", running.TryGetValue(name, out var s2) ? s2.ToString() : "Registered", isRunning));
            }
            catch { /* ignore */ }
        }
    }

    // ---------------------------------------------------------------- drivers

    private static void ScanDrivers(List<ScanItem> items)
    {
        string dir = Path.Combine(Environment.SystemDirectory, "drivers");
        if (!Directory.Exists(dir)) return;

        foreach (var file in SafeGet(() => Directory.GetFiles(dir, "rs*.sys"), Array.Empty<string>()))
        {
            string name = Path.GetFileName(file);
            string company = SafeGet(() => System.Diagnostics.FileVersionInfo.GetVersionInfo(file).CompanyName ?? "", "");
            if (!(company.Contains("Reason", StringComparison.OrdinalIgnoreCase) || DriverNameRx.IsMatch(name))) continue;

            string svc = Path.GetFileNameWithoutExtension(file);
            bool run = SafeGet(() =>
            {
                using var sc = new ServiceController(svc);
                return sc.Status == ServiceControllerStatus.Running;
            }, false);

            items.Add(new ScanItem(ScanCategory.Drivers, file, string.IsNullOrEmpty(company) ? "Driver" : company,
                run ? "Running" : "Present", run));
        }

        // Driver services registered in the registry whose file is already gone.
        using var svcRoot = TryOpen(RegistryHive.LocalMachine, RegistryView.Registry64, @"SYSTEM\CurrentControlSet\Services");
        if (svcRoot == null) return;
        foreach (var name in SafeGet(() => svcRoot.GetSubKeyNames(), Array.Empty<string>()))
        {
            if (!DriverNameRx.IsMatch(name)) continue;
            if (items.Any(i => i.Category == ScanCategory.Drivers && i.NameOrPath.EndsWith(name + ".sys", StringComparison.OrdinalIgnoreCase))) continue;
            items.Add(new ScanItem(ScanCategory.Drivers, $@"HKLM\SYSTEM\CurrentControlSet\Services\{name}", "Driver service key", "Registered", false));
        }
    }

    // ---------------------------------------------------------------- tasks

    private static void ScanTasks(List<ScanItem> items)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
        if (!Directory.Exists(dir)) return;

        var opts = new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in SafeGet(() => Directory.GetFiles(dir, "*", opts), Array.Empty<string>()))
        {
            try
            {
                string rel = Path.GetRelativePath(dir, file);
                if (rel.StartsWith("Microsoft" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.StartsWith("RAVAutoCleanTask", StringComparison.OrdinalIgnoreCase)) continue; // our own task

                bool match = NameRx.IsMatch(rel);
                if (!match && new FileInfo(file).Length < 512 * 1024)
                    match = Regex.IsMatch(File.ReadAllText(file), @"ReasonLabs", RegexOptions.IgnoreCase);
                if (match) items.Add(new ScanItem(ScanCategory.Tasks, file, rel, "Scheduled", false));
            }
            catch { /* unreadable task */ }
        }
    }

    // ---------------------------------------------------------------- shortcuts / policy

    private static void ScanShortcuts(List<ScanItem> items)
    {
        var dirs = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar")
        };

        var opts = new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var d in dirs.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var f in SafeGet(() => Directory.GetFiles(d, "*.lnk", opts), Array.Empty<string>()))
            {
                if (NameRx.IsMatch(Path.GetFileNameWithoutExtension(f)))
                    items.Add(new ScanItem(ScanCategory.Extras, f, "Shortcut", "Present", false));
            }
            // Start menu folders named after the product
            foreach (var sub in SafeGet(() => Directory.GetDirectories(d, "*", opts), Array.Empty<string>()))
            {
                if (NameRx.IsMatch(Path.GetFileName(sub)))
                    items.Add(new ScanItem(ScanCategory.Extras, sub, "Start menu folder", "Present", false));
            }
        }
    }

    private static void ScanBrowserPolicies(List<ScanItem> items)
    {
        string[] subs =
        {
            @"SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist",
            @"SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist",
            @"SOFTWARE\Policies\Google\Chrome\ExtensionSettings",
            @"SOFTWARE\Policies\Microsoft\Edge\ExtensionSettings"
        };
        foreach (var sub in subs)
        {
            // Policies are shared between registry views; the 64-bit view is sufficient.
            using var k = TryOpen(RegistryHive.LocalMachine, RegistryView.Registry64, sub);
            if (k == null) continue;
            foreach (var vn in SafeGet(() => k.GetValueNames(), Array.Empty<string>()))
            {
                var data = k.GetValue(vn)?.ToString() ?? "";
                if (NameRx.IsMatch(data) || NameRx.IsMatch(vn))
                    items.Add(new ScanItem(ScanCategory.Extras, $@"HKEY_LOCAL_MACHINE\{sub}\{vn}", data, "Policy", false));
            }
        }
    }

    // ---------------------------------------------------------------- defender

    private static string GetDefenderStatus()
    {
        var parts = new List<string>();

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct");
            foreach (ManagementBaseObject mo in searcher.Get())
            {
                string name = mo["displayName"]?.ToString() ?? "?";
                uint state = mo["productState"] is uint u ? u : 0;
                bool on = (state & 0xF000) == 0x1000;
                parts.Add($"{name} ({(on ? "On" : "Off")})");
                mo.Dispose();
            }
        }
        catch { /* SecurityCenter2 unavailable (Server / restricted) */ }

        try
        {
            using var sc = new ServiceController("WinDefend");
            parts.Add($"WinDefend: {sc.Status}");
        }
        catch { parts.Add("WinDefend: n/a"); }

        return string.Join("; ", parts);
    }
}
