# Verification — 24 October 2026

- Release build: **0 warnings, 0 errors**, .NET SDK 10.0.402.
- Automated checks: **31 passed, 0 failed**. Run the test executable with `--native --preview` from an interactive Windows desktop; a restricted sandbox may deny global hotkey registration.
- Both physical monitors exercised: main work area 3440 × 1392 at (0, 0); portrait work area 1080 × 1872 at (-1080, -81).
- Actual native window movement verified for repeated width cycling, centered half, undo, maximize, snapping from maximized, restoring maximized state, and transfers between monitors. Only disposable test windows were moved.
- Real Win+Alt+Left key events verified against the test window: exactly one action while held; no Start/menu opening when released. All default shortcuts were available after the Windows-reserved arrow fallback and Shift-number defaults were applied.
- Shortcut parsing, uniqueness, conflict detection, registration cleanup, message dispatch, recorder modifier handling, settings round trips, independent edits, and preservation of unreadable settings covered.
- Settings and custom position editor rendered and visually inspected. The custom editor's confirmation button is checked for clipping.
- Subsequent UI text/layout cleanup: release build and both form render checks passed.
- Position diagrams rendered and visually inspected for all 23 actions, the cycling shortcut editor, and the custom position editor.
- Published app successfully launched using the machine's existing .NET 10 Desktop Runtime. Current-user sign-in startup was enabled and verified.
- Silent launch followed by a second-instance settings request verified against the published app: settings visible and responding. The first-show visibility fix was rebuilt and checked after the automated suite.
- Initial tray-only sample: **47.8 MB working set**, **11.5 MB private memory**, **15.62 ms CPU time over 8 seconds** (about 0.20% of one CPU core). This short idle sample is a measurement, not a guarantee under every workload.

The portable package is generated in `dist/`. It contains the executable and its three companion runtime files; it requires the .NET 10 Desktop Runtime. Local settings, tool downloads, binaries, and render previews are excluded from Git.

## Startup repair — 25 October 2026

- The previous Run entry pointed to an existing executable, but the app was not running. No matching crash was found in the checked recent Application errors; the original missed launch remains unexplained.
- Version 0.1.1 replaces that entry with a current-user, least-privilege interactive sign-in task: 10-second delay, three failure retries at one-minute intervals, normal priority, battery operation, and no execution time limit. No password is stored.
- **37 checks passed, 0 failed**, including task policy, Windows-normalized account names/defaults, diagnostics rotation, and the original shortcut/window/preview checks. Release build: **0 warnings, 0 errors**.
- Registered the task through the published app; verified the old Run entry was removed. Started the task through Windows and confirmed its app process reached `ready`, with the task reported as Running.
- Verified a controlled quiet startup failure returns exit code 1. Verified a second quiet launch exits successfully without opening settings or creating another app instance.
- Disabled and re-enabled startup through the published app, verified task removal/recreation, then left WindowSnap running from the task. Saved settings remained byte-for-byte unchanged.
- Lifecycle diagnostics stay in `data/startup.log` and its rotated backup; both are excluded from Git.
- The next actual sign-in/reboot remains the final confirmation of the logon trigger. No reboot or sign-out was performed during verification.
