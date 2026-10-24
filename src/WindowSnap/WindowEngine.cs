using System.Diagnostics;
using System.Text;

namespace WindowSnap;

internal sealed class WindowEngine(bool allowOwnWindows = false)
{
    private sealed record Snapshot(Native.Placement Placement, Box Visible);
    private sealed class History(uint process, uint thread)
    {
        public uint Process { get; } = process;
        public uint Thread { get; } = thread;
        public List<Snapshot> Entries { get; } = [];
        public Box LastResult { get; set; }
        public bool LastMaximized { get; set; }
    }
    private readonly Dictionary<nint, History> histories = [];
    private (nint Window, string Id, int Index, Box Result, long Tick) cycle;

    public async Task<string?> ExecuteAsync(Binding binding, nint window, Configuration config)
    {
        if (!IsEligible(window, out uint process, out uint thread)) return null;
        // Bound responsiveness: never synchronously resize an unresponsive foreign window.
        if (Native.SendMessageTimeout(window, 0, 0, 0, 0x0002 | 0x0020, 150, out _) == 0)
            return "Window not responding.";
        var placement = Native.Placement.Create();
        if (!Native.GetWindowPlacement(window, ref placement)) return "Cannot read window position.";
        var before = Native.VisibleBounds(window);
        var monitors = Native.Monitors();
        int current = monitors.FindIndex(m => m.Handle == Native.MonitorFromWindow(window, 2));
        if (current < 0) return null;
        var work = monitors[current].Work;
        bool maximized = Native.IsZoomed(window);
        if (!histories.TryGetValue(window, out var history) || history.Process != process || history.Thread != thread)
        {
            if (histories.Count >= 64) histories.Remove(histories.Keys.First());
            histories[window] = history = new(process, thread);
        }
        if (binding.Kind == ActionKind.Undo)
        {
            cycle = default;
            if (history.Entries.Count == 0) return null;
            // Manual moves invalidate stale undo history, including most recycled HWND cases.
            if (history.LastMaximized != maximized || !maximized && !before.Near(history.LastResult, 8))
            { history.Entries.Clear(); return "Undo unavailable: window moved externally."; }
            var old = history.Entries[^1];
            if (Native.SetWindowPlacement(window, old.Placement))
            {
                history.Entries.RemoveAt(history.Entries.Count - 1);
                await Task.Delay(70);
                // WINDOWPLACEMENT uses workspace coordinates; Windows also handles monitor removal.
                history.LastResult = Native.VisibleBounds(window);
                history.LastMaximized = Native.IsZoomed(window);
                return null;
            }
            return "Cannot restore window.";
        }

        Box target;
        bool maximizeAfter = binding.Kind == ActionKind.Maximize || maximized && binding.Kind is ActionKind.PreviousMonitor or ActionKind.NextMonitor;
        int index = 0;
        switch (binding.Kind)
        {
            case ActionKind.Maximize:
                if (maximized) return null;
                target = work;
                break;
            case ActionKind.PreviousMonitor:
            case ActionKind.NextMonitor:
                if (monitors.Count < 2) return null;
                int next = Geometry.NextMonitor(monitors.Select(m => m.Work).ToArray(), current, binding.Kind == ActionKind.PreviousMonitor ? -1 : 1);
                target = maximized ? monitors[next].Work : Geometry.Place(monitors[next].Work, Geometry.Relative(before, work));
                break;
            case ActionKind.Center:
                int width = Math.Min(before.Width, work.Width), height = Math.Min(before.Height, work.Height);
                target = new(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height);
                break;
            default:
                if (binding.Regions.Count == 0) return null;
                if (config.CycleSizes && cycle.Window == window && cycle.Id == binding.Id && !maximized &&
                    Stopwatch.GetElapsedTime(cycle.Tick).TotalMilliseconds <= 1500 && before.Near(cycle.Result, 8))
                    index = (cycle.Index + 1) % binding.Regions.Count;
                target = Geometry.Place(work, binding.Regions[index], config.Gap);
                break;
        }
        if (!maximizeAfter && !maximized && before.Near(target)) return null;
        if (maximized)
        {
            Native.ShowWindowAsync(window, 9);
            for (int attempt = 0; attempt < 8 && Native.IsZoomed(window); attempt++) await Task.Delay(25);
            if (Native.IsZoomed(window)) return "Cannot unmaximize window.";
        }
        var snapshot = new Snapshot(placement, before);
        bool success;
        if (binding.Kind == ActionKind.Maximize)
        {
            Native.ShowWindowAsync(window, 3);
            await Task.Delay(70);
            success = Native.IsZoomed(window);
        }
        else
        {
            success = await MoveVisibleAsync(window, target);
            if (success && maximizeAfter) { Native.ShowWindowAsync(window, 3); await Task.Delay(70); }
        }
        var after = Native.VisibleBounds(window);
        if (!success || !maximizeAfter && !after.Near(target, 10))
        {
            // Some apps enforce minimum sizes. Keep undo when Windows did partially move them.
            if (!after.Near(before) || Native.IsZoomed(window) != maximized) Remember(history, snapshot, after, Native.IsZoomed(window));
            cycle = default;
            return "Resize failed: minimum size or elevated window.";
        }
        Remember(history, snapshot, after, Native.IsZoomed(window));
        cycle = binding.Kind == ActionKind.Position ? (window, binding.Id, index, after, Stopwatch.GetTimestamp()) : default;
        return null;
    }

    private static void Remember(History history, Snapshot snapshot, Box result, bool maximized)
    {
        history.Entries.Add(snapshot);
        if (history.Entries.Count > 20) history.Entries.RemoveAt(0);
        history.LastResult = result;
        history.LastMaximized = maximized;
    }

    private static async Task<bool> MoveVisibleAsync(nint window, Box target)
    {
        // A second pass accounts for invisible borders changing at a different monitor DPI.
        for (int pass = 0; pass < 3; pass++)
        {
            if (!Native.GetWindowRect(window, out var outer)) return false;
            var visible = Native.VisibleBounds(window);
            int left = Math.Clamp(visible.X - outer.Left, 0, 64), top = Math.Clamp(visible.Y - outer.Top, 0, 64);
            int right = Math.Clamp(outer.Right - visible.Right, 0, 64), bottom = Math.Clamp(outer.Bottom - visible.Bottom, 0, 64);
            if (!Native.SetWindowPos(window, 0, target.X - left, target.Y - top,
                target.Width + left + right, target.Height + top + bottom, Native.PositionFlags)) return false;
            await Task.Delay(55);
            if (Native.VisibleBounds(window).Near(target, 3)) return true;
        }
        return true;
    }

    private bool IsEligible(nint window, out uint process, out uint thread)
    {
        thread = Native.GetWindowThreadProcessId(window, out process);
        if (window == 0 || !Native.IsWindow(window) || !Native.IsWindowVisible(window) || Native.IsIconic(window) ||
            window == Native.GetShellWindow() || window == Native.GetDesktopWindow() || Native.GetAncestor(window, 2) != window ||
            !allowOwnWindows && process == Environment.ProcessId) return false;
        var name = new StringBuilder(256);
        Native.GetClassName(window, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "#32768") return false;
        uint style = (uint)Native.GetWindowLongPtr(window, -16).ToInt64();
        return (style & Native.Child) == 0 && (style & Native.Resizable) != 0;
    }
}
