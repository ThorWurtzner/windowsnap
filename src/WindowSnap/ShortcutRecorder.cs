using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WindowSnap;

// Exists only while the shortcut field has focus. Normal operation uses RegisterHotKey.
// Suppressing keys here prevents Win-key combinations from opening other Windows UI
// while the user is trying to record them.
internal sealed class ShortcutRecorder : IDisposable
{
    internal sealed class KeyState
    {
        private readonly HashSet<Keys> held;
        private readonly HashSet<Keys> swallowed = [];
        public KeyState(IEnumerable<Keys>? initiallyHeld = null) => held = initiallyHeld?.ToHashSet() ?? [];

        public (bool Suppress, Shortcut? Value) Process(Keys key, bool down)
        {
            if (!down) { held.Remove(key); return (swallowed.Remove(key), null); }
            bool first = held.Add(key);
            if (!first) return (swallowed.Contains(key), null);
            swallowed.Add(key);
            if (IsModifier(key)) return (true, null);
            uint mods = 0;
            foreach (var item in held)
                mods |= item switch
                {
                    Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => 2u,
                    Keys.Menu or Keys.LMenu or Keys.RMenu => 1u,
                    Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => 4u,
                    Keys.LWin or Keys.RWin => 8u,
                    _ => 0
                };
            return (true, new(mods, (uint)key));
        }

        internal static bool IsModifier(Keys key) => key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
            Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or Keys.LWin or Keys.RWin;
    }

    private readonly Native.KeyboardCallback callback;
    private readonly KeyState state;
    private readonly Func<bool> focused;
    private readonly Action<Shortcut> captured;
    private nint hook;

    public ShortcutRecorder(Func<bool> focused, Action<Shortcut> captured)
    {
        this.focused = focused;
        this.captured = captured;
        Keys[] modifiers = [Keys.LControlKey, Keys.RControlKey, Keys.LMenu, Keys.RMenu, Keys.LShiftKey, Keys.RShiftKey, Keys.LWin, Keys.RWin];
        state = new(modifiers.Where(key => Native.GetAsyncKeyState((int)key) < 0));
        callback = Handle;
        hook = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
        if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private nint Handle(int code, nint message, nint data)
    {
        if (code >= 0 && focused())
        {
            var key = (Keys)Marshal.ReadInt32(data);
            var result = state.Process(key, message.ToInt32() is 0x0100 or 0x0104);
            if (result.Value is { } value) captured(value);
            if (result.Suppress) return 1;
        }
        return Native.CallNextHookEx(hook, code, message, data);
    }

    public void Dispose()
    {
        if (hook == 0) return;
        Native.UnhookWindowsHookEx(hook);
        hook = 0;
        GC.KeepAlive(callback);
    }
}
