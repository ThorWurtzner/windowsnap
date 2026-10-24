using System.Diagnostics;
using System.Runtime.InteropServices;
using WindowSnap;
using Region = WindowSnap.Region;
using Shortcut = WindowSnap.Shortcut;
using Binding = WindowSnap.Binding;

internal static class Program
{
    private static int passed;
    private static int failed;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--preview-only"))
        {
            RenderPreview();
            return failed == 0 ? 0 : 1;
        }
        GeometryTests();
        SettingsTests();
        HotkeyTests();
        if (args.Contains("--native"))
        {
            using var probe = new Form
            {
                Text = "WindowSnap — disposable resize test", StartPosition = FormStartPosition.Manual,
                Bounds = new Rectangle(140, 160, 760, 500), MinimumSize = new Size(100, 100),
                ShowInTaskbar = false, Font = new Font("Segoe UI", 12)
            };
            probe.Controls.Add(new Label { Text = "WindowSnap is testing this window only.\nIt will close automatically.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
            probe.Shown += async (_, _) =>
            {
                try { await NativeTests(probe); }
                catch (Exception ex) { failed++; Console.WriteLine("FAIL native test: " + ex); }
                finally { probe.Close(); }
            };
            Application.Run(probe);
        }
        if (args.Contains("--preview")) RenderPreview();
        Console.WriteLine($"\n{passed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Test(string name, Action run)
    {
        try { run(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
    }
    private static void Check(bool condition, string message = "Assertion failed")
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual) where T : notnull => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}");

    private static void GeometryTests()
    {
        Test("ultrawide halves", () => Equal(new Box(1720, 0, 1720, 1400), Geometry.Place(new(0, 0, 3440, 1400), new(.5, 0, .5, 1))));
        Test("portrait monitor at negative coordinates", () => Equal(new Box(-1080, -400, 1080, 940), Geometry.Place(new(-1080, -400, 1080, 1880), new(0, 0, 1, .5))));
        Test("thirds share boundaries on odd widths", () =>
        {
            var work = new Box(-1201, 47, 3441, 1393);
            var a = Geometry.Place(work, new(0, 0, 1d / 3, 1));
            var b = Geometry.Place(work, new(1d / 3, 0, 1d / 3, 1));
            var c = Geometry.Place(work, new(2d / 3, 0, 1d / 3, 1));
            Equal(a.Right, b.X); Equal(b.Right, c.X); Equal(work.Right, c.Right);
        });
        Test("centered half keeps full height", () => Equal(new Box(860, 0, 1720, 1400), Geometry.Place(new(0, 0, 3440, 1400), new(.25, 0, .5, 1))));
        Test("three-quarter sizing", () => Equal(new Box(860, 0, 2580, 1400), Geometry.Place(new(0, 0, 3440, 1400), new(.25, 0, .75, 1))));
        Test("taskbar exclusion is retained", () => Equal(new Box(48, 24, 1872, 1016), Geometry.Place(new(48, 24, 1872, 1016), new(0, 0, 1, 1))));
        Test("inset cannot make a negative size", () => { var result = Geometry.Place(new(0, 0, 10, 10), new(0, 0, .1, .1), 64); Equal(new Box(0, 0, 1, 1), result); });
        Test("cross-monitor transfer preserves relative size", () =>
        {
            var region = Geometry.Relative(new(1720, 0, 1720, 1400), new(0, 0, 3440, 1400));
            Equal(new Box(-540, -300, 540, 1880), Geometry.Place(new(-1080, -300, 1080, 1880), region));
        });
        Test("offscreen or oversized transfers clamp inside monitor", () =>
            Equal(new Region(0, 0, 1, 1), Geometry.Relative(new(-100, -100, 5000, 2500), new(0, 0, 3440, 1400))));
        Test("monitor order follows physical left/right and wraps", () =>
        {
            Box[] monitors = [new(0, 0, 3440, 1400), new(-1080, -300, 1080, 1880)];
            Equal(1, Geometry.NextMonitor(monitors, 0, -1)); Equal(0, Geometry.NextMonitor(monitors, 1, 1));
            Equal(1, Geometry.NextMonitor(monitors, 0, 1));
        });
        Test("invalid geometry is rejected", () => Check(!new Region(.9, 0, .5, 1).IsValid && !new Region(double.NaN, 0, .5, 1).IsValid));
    }

    private static void SettingsTests()
    {
        Test("all default shortcuts are valid and unique", () =>
        {
            var config = Configuration.Defaults(); config.Validate();
            Equal(config.Bindings.Count, config.Bindings.Select(b => Shortcut.Parse(b.Shortcut)).Distinct().Count());
        });
        Test("defaults use Win+Alt and preserve ordinary AltGr typing", () =>
        {
            foreach (var binding in Configuration.Defaults().Bindings)
            {
                var shortcut = Shortcut.Parse(binding.Shortcut);
                Check((shortcut.Modifiers & 8) != 0 && (shortcut.Modifiers & 1) != 0 && (shortcut.Modifiers & 2) == 0, binding.Name + " must use Win+Alt");
            }
        });
        Test("shortcut normalization and modifier validation", () =>
        {
            Equal(Shortcut.Parse("ctrl+alt+1"), Shortcut.Parse("Alt+Ctrl+D1"));
            Equal("Win+Alt+Down", Shortcut.Parse("Win+Alt+Down").Display);
            Equal("Win+Alt+2", Shortcut.Parse("Win+Alt+D2").Display);
            foreach (string invalid in new[] { "Left", "Shift+Left", "Ctrl+F12", "Ctrl+ControlKey", "Ctrl+65535", "Ctrl+NotAKey" })
            {
                try { Shortcut.Parse(invalid); throw new InvalidOperationException("Accepted " + invalid); }
                catch (FormatException) { }
            }
        });
        Test("configuration clone is independent", () => { var original = Configuration.Defaults(); var copy = original.Clone(); copy.Bindings[0].Shortcut = ""; Check(original.Bindings[0].Shortcut.Length > 0); });
        Test("recorder captures Win+Alt+arrow and consumes matching releases", () =>
        {
            var recorder = new ShortcutRecorder.KeyState();
            Check(recorder.Process(Keys.LWin, true).Suppress);
            Check(recorder.Process(Keys.LMenu, true).Suppress);
            Equal(Shortcut.Parse("Win+Alt+Left"), recorder.Process(Keys.Left, true).Value!.Value);
            Check(recorder.Process(Keys.Left, true).Value is null, "Held keys should not repeat");
            Check(recorder.Process(Keys.Left, false).Suppress);
            Check(recorder.Process(Keys.LMenu, false).Suppress);
            Check(recorder.Process(Keys.LWin, false).Suppress);
        });
        Test("recorder does not swallow releases of modifiers held before focus", () =>
        {
            var recorder = new ShortcutRecorder.KeyState([Keys.LControlKey]);
            Equal(Shortcut.Parse("Ctrl+Left"), recorder.Process(Keys.Left, true).Value!.Value);
            Check(!recorder.Process(Keys.LControlKey, false).Suppress, "The OS needs the release of its already-held modifier");
        });
        Test("atomic settings round trip and corrupt-file preservation", () =>
        {
            string directory = Path.Combine(Environment.CurrentDirectory, ".local", "tests", Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SettingsStore(directory);
                var config = Configuration.Defaults(); config.Gap = 7; store.Save(config);
                Equal(7, store.Load().Gap);
                File.WriteAllText(store.FilePath, "{ broken");
                Equal(Configuration.Defaults().Bindings.Count, store.Load().Bindings.Count);
                Check(store.Warning is not null); Equal("{ broken", File.ReadAllText(store.FilePath));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }

    private static void HotkeyTests()
    {
        Test("focused shortcut capture installs and uninstalls", () =>
        {
            using var recorder = new ShortcutRecorder(() => false, _ => throw new InvalidOperationException("Unfocused input must not be recorded"));
            recorder.Dispose();
        });
        Test("global shortcut registration, conflict detection, and cleanup", () =>
        {
            var config = new Configuration { Bindings = [new Binding { Name = "Test", Shortcut = "Ctrl+Alt+Shift+F10", Kind = ActionKind.Maximize }] };
            using var first = new HotkeyWindow();
            using var second = new HotkeyWindow();
            var failures = first.Register(config);
            Check(failures.Count == 0, string.Join("; ", failures));
            Check(second.Register(config).Count == 1, "A conflict should be reported");
            first.Clear();
            Check(second.Register(config).Count == 0, "Clearing must release the shortcut");
            second.Clear();
        });
        Test("duplicate bindings are rejected before saving", () =>
        {
            var config = new Configuration { Bindings = [
                new Binding { Name = "One", Shortcut = "Ctrl+Alt+Shift+F10", Kind = ActionKind.Maximize },
                new Binding { Name = "Two", Shortcut = "Alt+Ctrl+Shift+F10", Kind = ActionKind.Undo }] };
            using var window = new HotkeyWindow();
            Check(window.ValidateAvailable(config, new Configuration()).Any(e => e.Contains("duplicate")));
        });
        Test("WM_HOTKEY dispatch uses the registered action", () =>
        {
            using var window = new HotkeyWindow();
            var config = new Configuration { Bindings = [new Binding { Id = "test", Shortcut = "Ctrl+Alt+Shift+F10", Kind = ActionKind.Maximize }] };
            var failures = window.Register(config);
            Check(failures.Count == 0, string.Join("; ", failures));
            string? received = null;
            window.Pressed = (binding, _) => received = binding.Id;
            Native.PostMessage(window.Handle, Native.HotkeyMessage, 1, 0);
            Application.DoEvents();
            Equal("test", received ?? "none");
        });
        using var defaults = new HotkeyWindow();
        var unavailable = defaults.Register(Configuration.Defaults());
        Console.WriteLine(unavailable.Count == 0 ? "Default shortcuts: all available on this desktop." :
            "Default shortcuts requiring a rebind: " + string.Join("; ", unavailable));
    }

    private static async Task NativeTests(Form probe)
    {
        await Task.Delay(150);
        var engine = new WindowEngine(true);
        var config = Configuration.Defaults();
        var window = probe.Handle;
        var original = Native.VisibleBounds(window);
        var monitors = Native.Monitors();
        var monitor = monitors.First(m => m.Handle == Native.MonitorFromWindow(window, 2));
        Console.WriteLine("Monitor work areas: " + string.Join("; ", monitors.Select(m => m.Work)));
        async Task Move(string id)
        {
            string? error = await engine.ExecuteAsync(config.Bindings.First(b => b.Id == id), window, config);
            Check(error is null, error ?? "");
        }
        async Task NativeCase(string name, Func<Task> test)
        {
            try { await test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        await NativeCase("native resize cycle: half → two-thirds → third → three-quarters", async () =>
        {
            foreach (var region in config.Bindings.First(b => b.Id == "left").Regions)
            {
                await Move("left");
                var target = Geometry.Place(monitor.Work, region);
                Check(Native.VisibleBounds(window).Near(target, 3), $"Expected {target}, got {Native.VisibleBounds(window)}");
            }
        });
        await NativeCase("undo restores all four native window placements", async () =>
        {
            for (int i = 0; i < 4; i++) await Move("undo");
            Check(Native.VisibleBounds(window).Near(original, 3), $"Expected {original}, got {Native.VisibleBounds(window)}");
        });
        await NativeCase("centered half and undo", async () =>
        {
            await Move("center-half");
            Check(Native.VisibleBounds(window).Near(Geometry.Place(monitor.Work, new(.25, 0, .5, 1)), 3));
            await Move("undo"); Check(Native.VisibleBounds(window).Near(original, 3));
        });
        await NativeCase("maximize, snap from maximized, and restore maximized state", async () =>
        {
            await Move("maximize"); Check(Native.IsZoomed(window), "Expected maximized");
            await Move("right-two-thirds"); Check(!Native.IsZoomed(window), "Expected restored");
            Check(Native.VisibleBounds(window).Near(Geometry.Place(monitor.Work, new(1d / 3, 0, 2d / 3, 1)), 3));
            await Move("undo"); Check(Native.IsZoomed(window), "Undo should restore maximized state");
            await Move("undo"); Check(!Native.IsZoomed(window));
        });
        if (monitors.Count > 1)
        {
            await NativeCase("transfer to portrait/other monitor and undo", async () =>
            {
                await Move("center-three-quarters");
                var source = Native.VisibleBounds(window);
                await Move("previous-monitor");
                Check(Native.MonitorFromWindow(window, 2) != monitor.Handle, "Expected another monitor");
                await Move("undo");
                Check(Native.VisibleBounds(window).Near(source, 5));
                await Move("undo");
            });
        }
        await NativeCase("real Win+Alt+Left input is handled once without opening Windows UI", async () =>
        {
            using var hotkeys = new HotkeyWindow();
            var binding = new Binding { Id = "physical-test", Name = "Physical shortcut test", Shortcut = "Win+Alt+Left", Regions = [new(0, 0, .5, 1)] };
            var completion = new TaskCompletionSource<string?>();
            int received = 0;
            hotkeys.Pressed = async (action, focused) =>
            {
                received++;
                try
                {
                    Check(focused == window, "Shortcut targeted a different window");
                    completion.TrySetResult(await engine.ExecuteAsync(action, focused, config));
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            };
            var failures = hotkeys.Register(new Configuration { Bindings = [binding] });
            Check(failures.Count == 0, string.Join("; ", failures));
            probe.Activate();
            TestNative.Focus(window);
            await Task.Delay(100);
            Check(Native.GetForegroundWindow() == window, "Test window must have focus before sending input");
            try
            {
                SendKey(Keys.LWin, false); SendKey(Keys.LMenu, false);
                await Task.Delay(30);
                SendKey(Keys.Left, false);
                await Task.Delay(30);
                SendKey(Keys.Left, false); // A held key must not repeat the action.
                await Task.Delay(30);
            }
            finally
            {
                SendKey(Keys.Left, true); SendKey(Keys.LMenu, true); SendKey(Keys.LWin, true);
            }
            string? error = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(error is null, error ?? "");
            await Task.Delay(100);
            Equal(1, received);
            Check(Native.GetForegroundWindow() == window, "Releasing the shortcut must not open Start or a menu");
            Check(Native.VisibleBounds(window).Near(Geometry.Place(monitor.Work, new(0, 0, .5, 1)), 3));
            await Move("undo");
        });
        await NativeCase("desktop and non-resizable dialogs are ignored", async () =>
        {
            var command = config.Bindings.First(b => b.Id == "left");
            Check(await engine.ExecuteAsync(command, Native.GetDesktopWindow(), config) is null);
            using var dialog = new Form { FormBorderStyle = FormBorderStyle.FixedDialog, ShowInTaskbar = false, Bounds = new Rectangle(200, 200, 350, 250) };
            dialog.Show(); await Task.Delay(70);
            var bounds = Native.VisibleBounds(dialog.Handle);
            Check(await engine.ExecuteAsync(command, dialog.Handle, config) is null);
            Equal(bounds, Native.VisibleBounds(dialog.Handle));
            dialog.Close();
        });
    }

    private static void SendKey(Keys key, bool up)
    {
        var input = new Native.Input { Type = 1, Data = new() { Keyboard = new() { Key = (ushort)key, Flags = (up ? 2u : 0) | (key is Keys.LWin or Keys.RWin or Keys.Left or Keys.Right or Keys.Up or Keys.Down ? 1u : 0) } } };
        Check(Native.SendInput(1, [input], (int)Marshal.SizeOf<Native.Input>()) == 1, "Windows blocked synthetic test input");
    }

    private static class TestNative
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
        public static void Focus(nint window)
        {
            uint current = Native.GetWindowThreadProcessId(window, out _);
            uint foreground = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
            bool attached = current != foreground && foreground != 0 && AttachThreadInput(current, foreground, true);
            try { SetForegroundWindow(window); }
            finally { if (attached) AttachThreadInput(current, foreground, false); }
        }
    }

    private static void RenderPreview()
    {
        Test("settings window renders", () =>
        {
            using var form = new SettingsForm(Configuration.Defaults(), SystemIcons.Application, null, (_, _) => null);
            form.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, form.ClientRectangle with { X = 0, Y = 0, Width = form.Width, Height = form.Height });
            string target = Path.Combine(Environment.CurrentDirectory, ".local", "settings-preview.png");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            bitmap.Save(target);
            form.Close();
            Console.WriteLine("Preview: " + target);
        });
        Test("custom position editor renders with its Done button visible", () =>
        {
            using var form = new BindingForm(new Binding { Custom = true, Regions = [new(.25, .1, .5, .8)] }, SystemIcons.Application);
            form.Show();
            Application.DoEvents();
            var done = form.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<Button>().Single(b => b.Text == "OK");
            var location = form.PointToClient(done.PointToScreen(Point.Empty));
            Check(location.Y + done.Height <= form.ClientSize.Height, "Done is clipped");
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
            string target = Path.Combine(Environment.CurrentDirectory, ".local", "custom-position-preview.png");
            bitmap.Save(target);
            form.Close();
            Console.WriteLine("Preview: " + target);
        });
    }
}
