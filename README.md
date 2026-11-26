# DisplayMosaic
<img width="256" height="256" alt="DisplayMosaic" src="https://github.com/user-attachments/assets/53bb12b7-2e8b-4265-9257-d992af47055f" />


Per-monitor wallpaper and VS Code theme switcher for Windows 10 and Windows 11.

DisplayMosaic lets you assign one wallpaper and VS Code theme preset to a designated primary/OLED monitor (matched by EDID model name or display code) and a separate preset to every other connected monitor (such as a laptop panel or secondary displays). It runs quietly in the taskbar hidden icons area, automatically re-applies presets when monitors are plugged or unplugged, and updates your VS Code theme in real time as you move your VS Code window between screens.

---

## What It Does

- **Distinct Wallpapers per Monitor** — Uses Windows' native `IDesktopWallpaper` COM API to set individual wallpapers across monitors (e.g., a pure-black or dark image on an OLED monitor and a standard wallpaper on all other monitors) with configurable fit mode (`Fill`, `Fit`, `Stretch`, `Tile`, `Center`, `Span`).
- **Dynamic VS Code Theme Switching** — Tracks which monitor your active VS Code / VS Code Insiders window is on and updates `workbench.colorTheme` in `settings.json` accordingly (with debouncing so dragging across screens doesn't flicker).
- **Automatic Display Hot-Plug Handling** — Monitors connected displays in the background and re-applies wallpapers and multi-monitor taskbar settings (`Show taskbar apps on: All taskbars`) whenever monitors are connected, disconnected, or woken from sleep.
- **Monitor Identification** — Queries EDID friendly names via WMI (`WmiMonitorID`) and provides an **Identify** overlay displaying large monitor numbers on each screen.
- **System Tray & Auto-Start** — Starts minimised in the taskbar hidden icons area at Windows sign-in. Closing or minimising the Settings window sends it back to the tray instead of quitting.

---

## Prerequisites

- **OS:** Windows 10 or Windows 11
- **Runtime & Compiler:** .NET Framework 4.x (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`), included out of the box with Windows 10 and Windows 11 — **no .NET SDK or Visual Studio download required**.
- **PowerShell:** Windows PowerShell 5.1 (built into Windows) to run the local build/install script.
- **Optional:** Visual Studio Code or Visual Studio Code Insiders (if using automatic theme switching).

---

## Installation

### Option 1: One-Click Install / Reinstall (Recommended)

Clone or download this repository to a permanent folder on your PC (for example `C:\Tools\DisplayMosaic`), then double-click **`Install.cmd`** (or run it from a terminal):

```powershell
.\Install.cmd
```

This script automatically:
1. Stops any running instance of `DisplayMosaic.exe`.
2. Compiles `DisplayMosaic.exe` locally from the source files in `src\`.
3. Creates a Windows Startup shortcut in `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\DisplayMosaic.lnk` with `--minimized --startup-delay`.
4. Launches `DisplayMosaic.exe` minimised in the taskbar hidden icons area.

> **Tip:** If `DisplayMosaic.exe` is ever deleted by cleanup tools or antivirus, simply run `Install.cmd` again to rebuild and restore everything from the local `src\` files.

### Option 2: PowerShell Installer Flags

You can also invoke `Install.ps1` directly with switches:

```powershell
# Build, register at Windows startup, and launch in the tray (default)
powershell -ExecutionPolicy Bypass -File .\Install.ps1

# Build only (do not register startup or launch)
powershell -ExecutionPolicy Bypass -File .\Install.ps1 -NoStartup -NoLaunch

# Build and launch without adding to Windows startup
powershell -ExecutionPolicy Bypass -File .\Install.ps1 -NoStartup

# Stop the running process and remove the Windows startup shortcut
powershell -ExecutionPolicy Bypass -File .\Install.ps1 -Uninstall
```

---

## Building Locally

No external build tools, NuGet packages, or `.csproj` restoration are required. You can compile `DisplayMosaic.exe` directly using Windows' built-in C# compiler (`csc.exe`):

```powershell
& "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /nologo /target:winexe /optimize+ /platform:anycpu `
  /out:DisplayMosaic.exe `
  /win32icon:src\DisplayMosaic.ico `
  /win32manifest:src\app.manifest `
  /reference:System.dll `
  /reference:System.Core.dll `
  /reference:System.Drawing.dll `
  /reference:System.Management.dll `
  /reference:System.Web.Extensions.dll `
  /reference:System.Windows.Forms.dll `
  src\*.cs
```

Or simply run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install.ps1 -NoStartup -NoLaunch
```

### Project Structure

```text
├── Install.cmd              # Double-click wrapper for building, installing startup, and launching
├── Install.ps1              # PowerShell build & installation script (uses built-in csc.exe)
├── README.md
└── src/
    ├── app.manifest         # Per-Monitor V2 DPI awareness & Windows Common Controls v6 manifest
    ├── DisplayMosaic.ico    # Application & system tray icon
    ├── MonitorService.cs    # Monitor enumeration, WMI EDID model lookup, wallpaper & taskbar apply
    ├── NativeWallpaper.cs   # IDesktopWallpaper COM interop & Win32 window-tracking P/Invokes
    ├── ProfileStore.cs      # JSON config persistence & VS Code settings.json theme updater
    ├── Program.cs           # Single-instance entry point, tray IPC & CLI flags
    ├── SettingsForm.cs      # WinForms settings UI
    ├── StartupManager.cs    # Windows Startup folder shortcut (.lnk) management
    └── TrayAppContext.cs    # System tray icon, context menu & background monitor watcher
```

---

## Usage

### Graphical Interface & System Tray

- **First run / Settings:** Double-click `DisplayMosaic.exe` (or click its icon in the taskbar hidden icons area) to open the **Settings** window.
- **Identify monitors:** Click **Identify** to flash monitor numbers across each connected display, then enter the **OLED monitor (model name or code)** (e.g., `MO27Q28G` or `GBT273C`).
- **Configure presets:** Choose a wallpaper image and VS Code theme name (as shown in VS Code's `Ctrl+K Ctrl+T` theme picker) for the matched OLED monitor and for all other monitors.
- **Save & apply:** Saves your configuration and immediately applies wallpapers and theme settings.
- **Tray menu (right-click the tray icon):**
  - **Open Settings**
  - **Apply Presets Now**
  - **Identify Monitors**
  - **Run at Startup** (toggles the Windows Startup shortcut)
  - **Exit**

### Configuration File

Settings are stored per-user in:

```text
%APPDATA%\DisplayMosaic\profiles.json
```

Example:

```json
{
  "position": "Fill",
  "oledMatch": "MO27Q28G",
  "oled": {
    "image": "C:\\Wallpapers\\oled-black.jpg",
    "theme": "Dark+ Pure Black (OLED)"
  },
  "other": {
    "image": "C:\\Wallpapers\\desktop.png",
    "theme": "Default Dark Modern"
  }
}
```

### CLI Options

`DisplayMosaic.exe` also supports command-line automation:

```text
DisplayMosaic.exe [options]
  (no args)          Opens Settings and stays minimised in taskbar hidden icons
  --minimized        Starts minimised in taskbar hidden icons and watches monitors
  --list             Shows connected monitors, OLED match, VS Code monitor, and presets
  --list-themes      Lists installed VS Code theme names
  --set              Updates presets (-Preset Oled|Other -Image <path> -Theme <name> -OledMatch <id> -Position <Fill|Fit|...>)
  --apply            Applies current presets once
  --install-startup  Installs Startup shortcut to run minimised at sign-in
  --remove-startup   Removes Startup shortcut
```

---

## Uninstalling

1. Run `.\Install.cmd -Uninstall` (or right-click the tray icon and choose **Exit**, then uncheck **Run at Startup** / delete the shortcut in `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\DisplayMosaic.lnk`).
2. Optionally delete `%APPDATA%\DisplayMosaic\` and the cloned repository folder.
