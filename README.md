# WindowSnap

Windows window manager with configurable global shortcuts and percentage-based positions.

## Run it

Open `dist/WindowSnap.exe`. Open its tray icon to change shortcuts and custom positions. Close or save settings to activate the shortcuts. Windows may place the tray icon in the overflow area by the clock.

Requires Windows 10/11 x64 and the **.NET 10 Desktop Runtime**. The executable is unsigned and runs without administrator privileges.

Settings are stored in `data/settings.json` beside the executable. Keep the app in a writable folder. After moving it, toggle **Start at sign-in** off and on to update the startup path.

## Shortcuts

Every shortcut is editable: select an action, choose **Change shortcut**, click the shortcut field, and press the desired keys. **Clear shortcut** leaves an action unassigned. Duplicates and shortcuts registered by another app are reported before saving. Settings suspend WindowSnap's shortcuts while open, including while recording a new one. Closing with Cancel discards the changes.

Monitor diagrams highlight each action's target area. The shortcut editor shows every width in a cycling action, in order. Custom position diagrams update as the percentages change.

The default modifier pair is **Win + Alt**:

| Keys after Win + Alt | Action |
| --- | --- |
| Left / Right | Left / right half; repeat within 1.5 s to cycle ½ → ⅔ → ⅓ → ¾ |
| Up | Maximize |
| Down | Undo the last WindowSnap move for that window |
| Shift + 1 / 2 / 3 | Left / middle / right third |
| Shift + 4 / 5 | Left / right two-thirds |
| Shift + 6 / 7 | Left / right three-quarters |
| Shift + 8 | Centered three-quarters, full height |
| C | Centered half, full height |
| Enter | Centered 75% width × 85% height |
| Space | Center without resizing |
| U / J | Top / bottom half — useful on the portrait monitor |
| Home / Page Up / End / Page Down | Top-left / top-right / bottom-left / bottom-right quarter |
| Shift + Left / Right | Previous / next monitor, in physical left-to-right order, wrapping at the ends |

**Add position** accepts left, top, width, and height as percentages of the current monitor's work area. For a centered 60% × 80% window: left 20%, top 10%, width 60%, height 80%.

The optional inset is measured in physical pixels on every position edge. Zero gives edge-to-edge snapping. Maximize uses Windows' actual maximized state and ignores the inset.

## How it behaves

- Uses the focused window's monitor and excludes that monitor's taskbar.
- Handles negative screen coordinates, portrait screens, and per-monitor DPI awareness. Compensates for invisible window borders; makes a bounded extra adjustment after a DPI change.
- Transferring monitors preserves proportional size and position. Maximized windows stay maximized. Monitor order follows desktop coordinates, so the portrait screen on the left is the previous monitor.
- Maintains up to 20 undo steps per window for up to 64 windows, in memory only. Manually moving a window invalidates its undo history to avoid restoring stale positions.
- Ignores the desktop, taskbars, menus, minimized windows, non-resizable dialogs, and its own settings window.
- One instance per desktop session. Launching it again opens settings. Tray controls let you pause or quit. Quitting releases every registered shortcut.
- Startup is a current-user Windows Run entry. Turning it off deletes only WindowSnap's entry. Closing the app leaves the sign-in preference in place; disable it before deleting the app.

## Resource use and limits

The idle process waits for Windows messages. It has **no polling loop, background scan, network code, telemetry, auto-updater, or third-party packages**. Most shortcuts use Windows' registered hotkeys. Recent Windows versions already reserve Win+Alt+arrows, so an event-driven keyboard handler supports those four explicitly assigned combinations when registration is blocked. It passes other keys through without logging, storing, or transmitting them. It is removed while settings are open, on pause, and on exit. These four combinations override Windows or other applications while WindowSnap is active. Number shortcuts include Shift to preserve Windows' taskbar menus.

A separate temporary key capture exists only while the shortcut field is focused, so recording Windows-key combinations does not trigger their normal actions. It is removed on focus loss or closing the editor. Moving a window uses short, bounded delays and a small serialized queue; holding a key does not repeatedly fire actions. Actual memory and CPU depend on Windows and the runtime; zero resource usage is not a promise.

Apps enforce their own minimum sizes, and normal-privilege software may not resize elevated apps. A hung app is skipped after a short response check. Exclusive full-screen games and special windows are not guaranteed to behave like normal desktop windows. WindowSnap does not change Windows' native mouse snapping settings. Global shortcuts reserve the combinations you assign; Windows-reserved combinations can conflict. Use the editor to choose alternatives. Win+Alt defaults avoid Ctrl+Alt / AltGr character conflicts on European keyboards.

## Build and test

```powershell
# Only if a .NET 10 SDK is not installed:
.\setup-build-tools.ps1

# Build the portable executable (uses the installed runtime):
.\build.ps1 -Test

# Also exercise Windows resizing against a disposable test window:
.\build.ps1 -NativeTests
```

The optional setup downloads Microsoft's pinned SDK archive, verifies its SHA-512 hash, and extracts it into `.tools/`. It does not install a system SDK. There are no NuGet packages to download. Build caches stay in `.tools/` and `bin/obj` directories. SDK telemetry and development certificate creation are disabled by the build script.

The test runner covers geometry for portrait/ultrawide/negative coordinates, odd-width rounding, monitor transfers, configuration persistence, corrupt configuration preservation, shortcut conflicts and dispatch. `-NativeTests` also checks real visible window bounds, cycling, maximize/restore, undo, and cross-monitor transfer when multiple monitors are available. It moves only windows created by the test runner.

Source is in `src/WindowSnap/`; the app uses C#/.NET 10, Windows Forms, and Win32 APIs. `NuGet.Config` intentionally has no package sources. `--settings`, `--quiet`, `--quit`, `--enable-startup`, `--disable-startup`, and `--data-dir <directory>` are supported for launching and development.
