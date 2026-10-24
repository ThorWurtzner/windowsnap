using System.Diagnostics;

namespace WindowSnap;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, @"Local\WindowSnap.Desktop", out bool first);
        if (!first)
        {
            nint existing = Native.FindWindowEx(new nint(-3), 0, null, "WindowSnap.Hotkeys");
            if (existing != 0) Native.PostMessage(existing, args.Contains("--quit") ? 0x8002u : 0x8001u, 0, 0);
            return;
        }
        if (args.Contains("--quit")) return;
        try
        {
            // Portable data lives with the executable; no installation or administrator access.
            string directory = Path.Combine(AppContext.BaseDirectory, "data");
            int dataArgument = Array.IndexOf(args, "--data-dir");
            if (dataArgument >= 0 && dataArgument + 1 < args.Length) directory = Path.GetFullPath(args[dataArgument + 1]);
            var store = new SettingsStore(directory);
            bool firstRun = !File.Exists(store.FilePath);
            var config = store.Load();
            if (firstRun) store.Save(config);
            if (args.Contains("--enable-startup")) Startup.Enabled = true;
            if (args.Contains("--disable-startup")) Startup.Enabled = false;
            using var context = new TrayContext(store, config);
            if (args.Contains("--settings") || firstRun && !args.Contains("--quiet")) context.ShowSettings();
            Application.Run(context);
        }
        catch (Exception ex)
        {
            MessageBox.Show("WindowSnap could not start.\n\n" + ex.Message, "WindowSnap", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { mutex.ReleaseMutex(); }
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private readonly SettingsStore store;
    private Configuration config;
    private readonly HotkeyWindow hotkeys = new();
    private readonly WindowEngine engine = new();
    private readonly NotifyIcon tray;
    private readonly Icon icon;
    private readonly ToolStripMenuItem pause = new("Pause shortcuts");
    private SettingsForm? settings;
    private List<string> failures = [];
    private readonly SemaphoreSlim serial = new(1);
    private int pending, generation;
    private bool paused, exiting;
    private long lastNotice;

    public TrayContext(SettingsStore store, Configuration config)
    {
        this.store = store;
        this.config = config;
        icon = CreateIcon();
        var menu = new ContextMenuStrip();
        menu.Items.Add("WindowSnap", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Shortcuts & settings…", null, (_, _) => ShowSettings());
        pause.Click += (_, _) =>
        {
            paused = !paused;
            generation++;
            if (paused) hotkeys.Clear(); else if (settings is null) failures = hotkeys.Register(this.config);
            UpdateStatus();
        };
        menu.Items.Add(pause);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit WindowSnap", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = icon, Text = "WindowSnap", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
        tray.BalloonTipClicked += (_, _) => ShowSettings();
        hotkeys.Pressed = (binding, window) => Dispatch(binding, window);
        hotkeys.InstanceMessage = quit => { if (quit) ExitThread(); else ShowSettings(); };
        failures = hotkeys.Register(config);
        UpdateStatus();
        if (failures.Count > 0 || store.Warning is not null) Notify("Some shortcuts need attention. Open settings from the tray.");
    }

    public void ShowSettings()
    {
        if (exiting) return;
        if (settings is not null) { settings.Show(); Native.ShowWindowAsync(settings.Handle, 1); settings.Activate(); return; }
        generation++;
        hotkeys.Clear();
        string? warning = store.Warning ?? (failures.Count == 0 ? null : string.Join("\n", failures));
        settings = new SettingsForm(config, icon, warning, Save);
        settings.FormClosed += (_, _) =>
        {
            settings.Dispose();
            settings = null;
            generation++;
            if (!paused && !exiting) failures = hotkeys.Register(config);
            UpdateStatus();
        };
        settings.Show();
        // A silent tray launch can inherit STARTUPINFO's hidden first-show state.
        Native.ShowWindowAsync(settings.Handle, 1);
        settings.Activate();
        UpdateStatus();
    }

    private string? Save(Configuration candidate, bool startAtLogin)
    {
        try
        {
            candidate.Validate();
            var errors = hotkeys.ValidateAvailable(candidate, config);
            hotkeys.Clear();
            if (errors.Count > 0) return string.Join("\n", errors);
            bool previousStartup = Startup.Enabled;
            try
            {
                if (previousStartup != startAtLogin) Startup.Enabled = startAtLogin;
                store.Save(candidate);
            }
            catch
            {
                if (previousStartup != startAtLogin) Startup.Enabled = previousStartup;
                throw;
            }
            config = candidate;
            failures.Clear();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.SecurityException)
        { return "Could not save settings: " + ex.Message; }
    }

    private async void Dispatch(Binding binding, nint window)
    {
        if (pending >= 4 || paused || settings is not null || exiting) return;
        pending++;
        int version = generation;
        await serial.WaitAsync();
        try
        {
            if (generation != version || exiting || Native.GetForegroundWindow() != window) return;
            string? issue = await Task.Run(() => engine.ExecuteAsync(binding, window, config));
            if (issue is not null && !exiting) Notify(issue);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!exiting) Notify("Could not move this window. " + ex.Message); }
        finally { pending--; serial.Release(); }
    }

    private void UpdateStatus()
    {
        pause.Checked = paused;
        if (exiting) return;
        tray.Text = paused ? "WindowSnap — paused" : settings is not null ? "WindowSnap — editing shortcuts" : failures.Count > 0 ? "WindowSnap — shortcut conflict" : "WindowSnap — ready";
    }

    private void Notify(string message)
    {
        if (lastNotice != 0 && Stopwatch.GetElapsedTime(lastNotice).TotalSeconds < 15) return;
        lastNotice = Stopwatch.GetTimestamp();
        tray.ShowBalloonTip(5000, "WindowSnap", message, ToolTipIcon.Info);
    }

    protected override void ExitThreadCore()
    {
        exiting = true;
        generation++;
        settings?.Close();
        hotkeys.Dispose();
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose();
        icon.Dispose();
        base.ExitThreadCore();
    }

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var background = new SolidBrush(Color.FromArgb(36, 88, 198));
            graphics.FillRoundedRectangle(background, new Rectangle(1, 1, 30, 30), new Size(6, 6));
            using var white = new Pen(Color.White, 2);
            graphics.DrawRectangle(white, 7, 8, 18, 16);
            graphics.DrawLine(white, 19, 8, 19, 24);
        }
        nint handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { Native.DestroyIcon(handle); }
    }
}
