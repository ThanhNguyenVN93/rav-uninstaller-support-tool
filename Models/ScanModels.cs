namespace RavUninstallerTool.Models;

public enum ScanCategory { Folders, Registry, Services, Drivers, Tasks, Extras }

public record ScanItem(ScanCategory Category, string NameOrPath, string Detail, string State, bool IsCritical);

public record ScanResult(
    List<ScanItem> Items,
    string? UninstallExecutablePath,
    string? UninstallArgs,
    bool HasActiveDriversOrServices,
    bool IsWindows10OrHigher,
    string OsDescription,
    string Architecture,
    string DefenderStatus,
    bool IsSafeMode);

public enum AppPhase { NormalInit, SafeModeCleaning, PostCleanVerify }

public class WorkflowState
{
    public AppPhase Phase { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? OriginalExePath { get; set; }
    public string? LogFilePath { get; set; }
}
