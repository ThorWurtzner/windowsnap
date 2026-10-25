using System.Diagnostics;

namespace WindowSnap;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Mutex? mutex = null;
        bool ownsMutex = false;
        bool quiet = args.Contains("--quiet");
        try
        {
            // Portable data lives with the executable; no installation or administrator access.
            string directory = Path.Combine(AppContext.BaseDirectory, "data");
            int dataArgument = Array.IndexOf(args, "--data-dir");
            if (dataArgument >= 0 && dataArgument + 1 < args.Length) directory = Path.GetFullPath(args[dataArgument + 1]);
            StartupLog.Initialize(directory);
            StartupLog.Write($"launch mode={(args.Contains("--startup") ? "sign-in" : "manual")} version={typeof(Program).Assembly.GetName().Version}");
            AppDomain.CurrentDomain.UnhandledException += (_, e) => StartupLog.Write("unhandled exception", e.ExceptionObject as Exception);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            ApplicationConfiguration.Initialize();
            // Explicit preference changes must also work while the tray instance is running.
            if (args.Contains("--enable-startup")) Startup.Enabled = true;
            if (args.Contains("--disable-startup")) Startup.Enabled = false;
            mutex = new Mutex(true, @"Local\WindowSnap.Desktop", out ownsMutex);
            if (!ownsMutex)
            {
                nint existing = Native.FindWindowEx(new nint(-3), 0, null, "WindowSnap.Hotkeys");
                if (existing != 0 && (args.Contains("--quit") || args.Contains("--settings") || !quiet))
                    Native.PostMessage(existing, args.Contains("--quit") ? 0x8002u : 0x8001u, 0, 0);
                StartupLog.Write("existing instance");
                return 0;
            }
            if (args.Contains("--quit")) return 0;
            var store = new SettingsStore(directory);
            bool firstRun = !File.Exists(store.FilePath);
            var config = store.Load();
            if (firstRun) store.Save(config);
            using var context = new TrayContext(store, config);
            if (args.Contains("--settings") || firstRun && !quiet) context.ShowSettings();
            StartupLog.Write("ready");
            Application.Run(context);
            StartupLog.Write("exit");
            return 0;
        }
        catch (Exception ex)
        {
            StartupLog.Write("startup failed", ex);
            if (!quiet) MessageBox.Show("Startup failed.\n\n" + ex.Message, "WindowSnap", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            if (ownsMutex) mutex?.ReleaseMutex();
            mutex?.Dispose();
        }
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
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        pause.Click += (_, _) =>
        {
            paused = !paused;
            generation++;
            if (paused) hotkeys.Clear(); else if (settings is null) failures = hotkeys.Register(this.config);
            UpdateStatus();
        };
        menu.Items.Add(pause);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = icon, Text = "WindowSnap", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
        tray.BalloonTipClicked += (_, _) => ShowSettings();
        hotkeys.Pressed = (binding, window) => Dispatch(binding, window);
        hotkeys.InstanceMessage = quit => { if (quit) ExitThread(); else ShowSettings(); };
        failures = hotkeys.Register(config);
        UpdateStatus();
        if (failures.Count > 0 || store.Warning is not null) Notify("Shortcut or settings error. Open Settings.");
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.SecurityException or
            System.Runtime.InteropServices.COMException or InvalidOperationException)
        { return "Save failed: " + ex.Message; }
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
        { if (!exiting) Notify("Move failed: " + ex.Message); }
        finally { pending--; serial.Release(); }
    }

    private void UpdateStatus()
    {
        pause.Checked = paused;
        if (exiting) return;
        tray.Text = paused ? "WindowSnap — paused" : settings is not null ? "WindowSnap — settings" : failures.Count > 0 ? "WindowSnap — shortcut conflict" : "WindowSnap";
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
