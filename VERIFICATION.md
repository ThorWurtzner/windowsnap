# Verification — 24 October 2026

- Release build: **0 warnings, 0 errors**, .NET SDK 10.0.402.
- Automated checks: **31 passed, 0 failed**. Run the test executable with `--native --preview` from an interactive Windows desktop; a restricted sandbox may deny global hotkey registration.
- Both physical monitors exercised: main work area 3440 × 1392 at (0, 0); portrait work area 1080 × 1872 at (-1080, -81).
- Actual native window movement verified for repeated width cycling, centered half, undo, maximize, snapping from maximized, restoring maximized state, and transfers between monitors. Only disposable test windows were moved.
- Real Win+Alt+Left key events verified against the test window: exactly one action while held; no Start/menu opening when released. All default shortcuts were available after the Windows-reserved arrow fallback and Shift-number defaults were applied.
- Shortcut parsing, uniqueness, conflict detection, registration cleanup, message dispatch, recorder modifier handling, settings round trips, independent edits, and preservation of unreadable settings covered.
- Settings and custom position editor rendered and visually inspected. The custom editor's confirmation button is checked for clipping.
- Subsequent UI text/layout cleanup: release build and both form render checks passed.
- Published app successfully launched using the machine's existing .NET 10 Desktop Runtime. Current-user sign-in startup was enabled and verified.
- Silent launch followed by a second-instance settings request verified against the published app: settings visible and responding. The first-show visibility fix was rebuilt and checked after the automated suite.
- Initial tray-only sample: **47.8 MB working set**, **11.5 MB private memory**, **15.62 ms CPU time over 8 seconds** (about 0.20% of one CPU core). This short idle sample is a measurement, not a guarantee under every workload.

The portable package is generated in `dist/`. It contains the executable and its three companion runtime files; it requires the .NET 10 Desktop Runtime. Local settings, tool downloads, binaries, and render previews are excluded from Git.
