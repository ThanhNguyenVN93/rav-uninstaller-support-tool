# ==============================================================================
# SCRIPT QUET & GO SACH RAV ENDPOINT PROTECTION / REASONLABS
# Ho tro chay thu cong (Interactive) va chay ngam tu dong (-Auto) trong Safe Mode
# ==============================================================================
param(
    [switch]$Auto,
    [string]$LogFile = ""
)

function Write-Log {
    param(
        [string]$Message,
        [string]$Color = "Gray"
    )
    $Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $Line = "[$Timestamp] $Message"
    if (-not $Auto) {
        Write-Host $Message -ForegroundColor $Color
    }
    if ($LogFile -and $LogFile.Trim() -ne "") {
        try {
            $LogDir = Split-Path -Path $LogFile -Parent
            if ($LogDir -and -not (Test-Path $LogDir)) {
                New-Item -Path $LogDir -ItemType Directory -Force | Out-Null
            }
            Add-Content -Path $LogFile -Value $Line -Encoding UTF8 -ErrorAction SilentlyContinue
        } catch { }
    }
}

function Exit-WithPause {
    param([int]$Code = 0)
    if (-not $Auto) {
        Pause
    }
    exit $Code
}

function Remove-LockedFileOrScheduleReboot {
    param([string]$FilePath)
    if (-not (Test-Path $FilePath)) { return }
    try {
        Remove-Item -Path $FilePath -Force -ErrorAction Stop
        Write-Log "[-] Da xoa file: $FilePath" "Gray"
    } catch {
        # Fallback: danh dau xoa khi reboot bang PendingFileRenameOperations
        try {
            $RegPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager"
            $Pending = (Get-ItemProperty -Path $RegPath -Name "PendingFileRenameOperations" -ErrorAction SilentlyContinue).PendingFileRenameOperations
            if ($null -eq $Pending) { $Pending = @() }
            $Pending += "\??\$FilePath"
            $Pending += ""
            Set-ItemProperty -Path $RegPath -Name "PendingFileRenameOperations" -Value ([string[]]$Pending) -Type MultiString -Force
            Write-Log "[!] File dang bi khoa, da hen xoa khi khoi dong lai: $FilePath" "Yellow"
        } catch {
            Write-Log "[!] Khong the xoa file: $FilePath ($($_.Exception.Message))" "Red"
        }
    }
}

# 1. Kiem tra quyen Administrator
$CurrentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $CurrentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Log "Vui long chay PowerShell voi quyen Administrator (Run as Administrator)!" "Yellow"
    Exit-WithPause 1
}

if (-not $Auto) {
    Clear-Host
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "   CONG CU QUET & DON SACH RAV / REASONLABS TREN WINDOWS  " -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan
}
Write-Log "[*] Bat dau quet he thong (Auto=$Auto)..." "Gray"

# --- BUOC 1: QUET & LIET KE ---

# 1.1. Quet Folder
$TargetPaths = @(
    "C:\Program Files",
    "C:\Program Files (x86)",
    "C:\ProgramData",
    "$env:LOCALAPPDATA",
    "$env:APPDATA",
    "$env:TEMP"
) | Where-Object { $_ -and (Test-Path $_) }

# Pattern an toan (khong match nham "TimeTravel", "Brave", ...)
$SafePattern = 'ReasonLabs|ReasonEDR|Reason\s*Cyber|(^|[^A-Za-z])RAV([^A-Za-z]|$)'

$FoundFolders = Get-ChildItem -Path $TargetPaths -Directory -Recurse -Depth 2 -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.Name -match $SafePattern -and
                    $_.FullName -notmatch '\\ProgramData\\Microsoft(\\|$)' -and
                    $_.Name -ne 'RAVCleaner'
                }

# 1.2. Quet Registry ung dung & Uninstall & Startup
$RegAppPaths = @(
    "HKLM:\SOFTWARE\ReasonLabs",
    "HKLM:\SOFTWARE\WOW6432Node\ReasonLabs",
    "HKCU:\SOFTWARE\ReasonLabs"
)
$FoundRegKeys = @()
foreach ($Path in $RegAppPaths) {
    if (Test-Path $Path) { $FoundRegKeys += Get-Item -Path $Path -ErrorAction SilentlyContinue }
}

$UninstallPaths = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
)
$FoundUninstallKeys = Get-ChildItem -Path $UninstallPaths -ErrorAction SilentlyContinue |
                      Where-Object { $_.GetValue("DisplayName") -match $SafePattern }

# 1.3. Quet Services & Drivers trong Registry (sau hon Get-Service)
$FoundServices = Get-Service -ErrorAction SilentlyContinue |
                 Where-Object { $_.Name -match $SafePattern -or $_.DisplayName -match $SafePattern }

$ServiceRegRoot = "HKLM:\SYSTEM\CurrentControlSet\Services"
$FoundServiceRegKeys = Get-ChildItem -Path $ServiceRegRoot -ErrorAction SilentlyContinue | Where-Object {
    $Img = $_.GetValue("ImagePath")
    ($_.PSChildName -match "^(RAV|Reason|rsCamFilter|rsElam|rsRTP)") -or ($Img -and $Img -match "$SafePattern|rsCamFilter|rsElam|rsRTP")
}

$FoundDrivers = Get-ChildItem "C:\Windows\System32\drivers" -Filter "*rs*.sys" -ErrorAction SilentlyContinue |
                Where-Object { $_.VersionInfo.CompanyName -match "Reason" -or $_.BaseName -match "^(rsCamFilter|rsElam|rsRTP)" }

$FoundTaskFiles = Get-ChildItem -Path "C:\Windows\System32\Tasks" -Recurse -File -ErrorAction SilentlyContinue |
                  Where-Object { ($_.Name -match $SafePattern -or $_.DirectoryName -match $SafePattern) -and $_.Name -ne "RAVAutoCleanTask" }

# 1.4. Quet Shortcut & Browser Extension ForceList Policy
$ShortcutRoots = @(
    [Environment]::GetFolderPath("Desktop"),
    [Environment]::GetFolderPath("CommonDesktopDirectory"),
    [Environment]::GetFolderPath("StartMenu"),
    [Environment]::GetFolderPath("CommonStartMenu"),
    "$env:APPDATA\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"
) | Where-Object { $_ -and (Test-Path $_) }

$FoundShortcuts = Get-ChildItem -Path $ShortcutRoots -Recurse -ErrorAction SilentlyContinue |
                  Where-Object { $_.Name -match $SafePattern }

$TotalFound = @($FoundFolders).Count + @($FoundRegKeys).Count + @($FoundUninstallKeys).Count +
              @($FoundServices).Count + @($FoundServiceRegKeys).Count + @($FoundDrivers).Count +
              @($FoundTaskFiles).Count + @($FoundShortcuts).Count

if ($TotalFound -eq 0) {
    Write-Log "[+] He thong da sach hoan toan! Khong tim thay dau vet nao cua RAV / ReasonLabs." "Green"
    Exit-WithPause 0
}

if (-not $Auto) {
    Write-Host ">>> Tim thay $TotalFound thanh phan RAV / ReasonLabs:" -ForegroundColor Yellow
    $FoundFolders | ForEach-Object { Write-Host "  [Folder] $($_.FullName)" }
    $FoundRegKeys | ForEach-Object { Write-Host "  [Reg] $($_.PSPath -replace 'Microsoft.PowerShell.Core\\Registry::','')" }
    $FoundUninstallKeys | ForEach-Object { Write-Host "  [Uninstall] $($_.GetValue('DisplayName'))" }
    $FoundServices | ForEach-Object { Write-Host "  [Service] $($_.Name)" }
    $FoundDrivers | ForEach-Object { Write-Host "  [Driver] $($_.FullName)" }
    $FoundTaskFiles | ForEach-Object { Write-Host "  [Task] $($_.FullName)" }
    $FoundShortcuts | ForEach-Object { Write-Host "  [Shortcut] $($_.FullName)" }

    Write-Host "`n==========================================================" -ForegroundColor Red
    $Confirm = Read-Host "Ban co chac chan muon XOA VINH VIEN toan bo muc liet ke o tren khong? (Y/N)"
    if ($Confirm -notmatch "^[Yy]$") {
        Write-Host "`n[!] Da huy thao tac xoa." -ForegroundColor Cyan
        Exit-WithPause 0
    }
}

# --- BUOC 2: THUC THI XOA SACH ---
Write-Log "[*] Dang tien hanh go bo va don dep..." "Cyan"

# 2.1. Dung tien trinh dang chay
Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -match $SafePattern } | ForEach-Object {
    Write-Log "[-] Dung tien trinh: $($_.Name)" "Gray"
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}

# 2.2. Huy dang ky va xoa Services
foreach ($Svc in $FoundServices) {
    Write-Log "[-] Xoa Service: $($Svc.Name)" "Gray"
    Stop-Service -Name $Svc.Name -Force -ErrorAction SilentlyContinue
    sc.exe delete $Svc.Name 2>$null | Out-Null
}

# 2.3. Huy dang ky va xoa Drivers (.sys) + Service Registry keys
$KnownDriverNames = @("rsCamFilter020600", "rsElam", "rsRTP")
foreach ($DrvName in $KnownDriverNames) {
    sc.exe stop $DrvName 2>$null | Out-Null
    sc.exe delete $DrvName 2>$null | Out-Null
}
foreach ($SvcReg in $FoundServiceRegKeys) {
    $SvcName = $SvcReg.PSChildName
    sc.exe stop $SvcName 2>$null | Out-Null
    sc.exe delete $SvcName 2>$null | Out-Null
    Remove-Item -Path $SvcReg.PSPath -Recurse -Force -ErrorAction SilentlyContinue
    Write-Log "[-] Da xoa Service/Driver Key: $SvcName" "Gray"
}
foreach ($Drv in $FoundDrivers) {
    Remove-LockedFileOrScheduleReboot -FilePath $Drv.FullName
}

# 2.4. Xoa Scheduled Tasks
Get-ScheduledTask -ErrorAction SilentlyContinue |
    Where-Object { ($_.TaskName -match $SafePattern -or $_.TaskPath -match $SafePattern) -and $_.TaskName -ne "RAVAutoCleanTask" } |
    ForEach-Object {
        Write-Log "[-] Unregister Task: $($_.TaskPath)$($_.TaskName)" "Gray"
        Unregister-ScheduledTask -TaskName $_.TaskName -TaskPath $_.TaskPath -Confirm:$false -ErrorAction SilentlyContinue
    }
foreach ($Task in $FoundTaskFiles) {
    Remove-Item -Path $Task.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

# 2.5. Xoa Shortcut & Icon
foreach ($Sc in $FoundShortcuts) {
    Write-Log "[-] Xoa Shortcut: $($Sc.FullName)" "Gray"
    Remove-Item -Path $Sc.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

# 2.6. Xoa Startup Run keys & Browser Extension ForceList Policy
$RunKeys = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
    "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
)
foreach ($RK in $RunKeys) {
    if (Test-Path $RK) {
        $Props = Get-ItemProperty -Path $RK -ErrorAction SilentlyContinue
        foreach ($P in $Props.PSObject.Properties) {
            if ($P.Name -match $SafePattern -or ($P.Value -is [string] -and $P.Value -match $SafePattern)) {
                Remove-ItemProperty -Path $RK -Name $P.Name -Force -ErrorAction SilentlyContinue
                Write-Log "[-] Xoa Startup Entry: $RK\$($P.Name)" "Gray"
            }
        }
    }
}

$PolicyKeys = @(
    "HKLM:\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist",
    "HKLM:\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist"
)
foreach ($PK in $PolicyKeys) {
    if (Test-Path $PK) {
        $Props = Get-ItemProperty -Path $PK -ErrorAction SilentlyContinue
        foreach ($P in $Props.PSObject.Properties) {
            if ($P.Value -is [string] -and $P.Value -match $SafePattern) {
                Remove-ItemProperty -Path $PK -Name $P.Name -Force -ErrorAction SilentlyContinue
                Write-Log "[-] Xoa Browser Extension Policy: $PK\$($P.Name)" "Gray"
            }
        }
    }
}

# 2.7. Xoa Folder
foreach ($Folder in $FoundFolders) {
    Write-Log "[-] Xoa Folder: $($Folder.FullName)" "Gray"
    try {
        Remove-Item -Path $Folder.FullName -Recurse -Force -ErrorAction Stop
    } catch {
        Write-Log "[!] Canh bao xoa folder $($Folder.FullName): $($_.Exception.Message)" "Yellow"
    }
}

# 2.8. Xoa Registry ung dung & Uninstall keys
foreach ($Reg in $FoundRegKeys) {
    Write-Log "[-] Xoa Registry: $($Reg.PSPath -replace 'Microsoft.PowerShell.Core\\Registry::','')" "Gray"
    Remove-Item -Path $Reg.PSPath -Recurse -Force -ErrorAction SilentlyContinue
}
foreach ($UKey in $FoundUninstallKeys) {
    Write-Log "[-] Xoa Uninstall Key: $($UKey.GetValue('DisplayName'))" "Gray"
    Remove-Item -Path $UKey.PSPath -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Log "[+] HOAN TAT! Da don dep xong RAV / ReasonLabs." "Green"
Exit-WithPause 0
