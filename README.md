# RAV Uninstaller Support Tool

[![Release](https://img.shields.io/github/v/release/ThanhNguyenVN93/rav-uninstaller-support-tool)](https://github.com/ThanhNguyenVN93/rav-uninstaller-support-tool/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20(x64)-0078D6)](https://github.com/ThanhNguyenVN93/rav-uninstaller-support-tool/releases)
[![.NET](https://img.shields.io/badge/.NET-10.0%20Single--File-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

An open-source **.NET 10 WinForms** (`Guna.UI2`) utility to inspect and completely remove **RAV Endpoint Protection / ReasonLabs** on **Windows 10 & Windows 11 (64-bit)** — including kernel drivers, services, registry keys, scheduled tasks, shortcuts, and forced browser extension policies.

---

## Features

- **Instant Presence Check:** Exits cleanly with a notice if RAV / ReasonLabs is not installed (`0` items found).
- **Automatic System Restore Point:** Silently creates a `"Before RAV Removal"` restore point on load (enables System Restore on `C:` if needed; non-fatal if blocked by policy).
- **6-Category Deep Scan (64-bit & 32-bit Registry Views):**
  - **Folders** (`Program Files`, `Program Files (x86)`, `ProgramData`, `LocalAppData`, `AppData`, `Temp`)
  - **Registry** (`ReasonLabs` & `Uninstall` keys)
  - **Services** (`ServiceController` + `HKLM\SYSTEM\CurrentControlSet\Services` `ImagePath`)
  - **Kernel Drivers** (`rsCamFilter020600.sys`, `rsElam.sys`, `rsRTP.sys`, etc.)
  - **Scheduled Tasks** (`C:\Windows\System32\Tasks`)
  - **Shortcuts & Browser Policies** (Desktop, Start Menu, Taskbar Pinned, Startup `Run`, Chrome/Edge `ExtensionInstallForcelist`)
- **3 Removal Modes:**
  1. **Manual Uninstall (`Uninstall.exe`):** Launches the vendor's official uninstaller.
  2. **Quick Clean Remnants (No Reboot):** Removes leftover files, registry keys, tasks, shortcuts, and policies immediately without restarting.
  3. **Auto Safe Mode + Clean All:** Copies the tool & embedded `RAV.ps1` to `C:\ProgramData\RAVCleaner`, registers an elevated one-time Safe Mode entry (`*RAVAutoClean`), boots into `safeboot minimal`, **immediately deletes the `safeboot` flag first** (preventing boot loops), runs `RAV.ps1 -Auto`, reboots back to normal Windows, verifies `0` items remain + checks **Windows Defender (`WinDefend`)** status, saves `RAVCleaner.log` to the Desktop, and cleans up temporary files.
- **Bilingual & Theme Support:** Live **VI / EN** language toggle and **Light / Dark** mode.
- **Built-In Auto-Update:** Checks GitHub Releases, verifies `SHA256SUMS.txt`, extracts safely (Zip-Slip protected), and swaps the running executable.

---

## Download

Download the self-contained single-file `RavUninstallerTool.exe` (no separate .NET runtime required) from the official Releases page:

- **Latest Release:** https://github.com/ThanhNguyenVN93/rav-uninstaller-support-tool/releases/latest

---

## CLI Flags (for Technicians)

```text
RavUninstallerTool.exe --scan            # Open UI and scan system
RavUninstallerTool.exe --clean-remnants  # Run quick remnant cleanup
RavUninstallerTool.exe --auto-safemode   # Prepare Safe Mode cleanup and reboot
```

---

## Build from Source

Requires **.NET 10 SDK** on Windows:

```powershell
# Run unit tests (UpdateService & Localization parity)
dotnet run --project SelfTest\SelfTest.csproj

# Publish self-contained single-file executable (win-x64)
dotnet publish RavUninstallerTool.csproj -c Release -r win-x64
```

Output executable: `bin\Release\net10.0-windows\win-x64\publish\RavUninstallerTool.exe`

---

## License

Licensed under the [MIT License](LICENSE).
