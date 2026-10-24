using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WindowSnap;

// Windows itself owns Win+Alt+arrows on recent releases. Intercept only these
// explicitly assigned combinations, without changing Explorer or registry policies.
internal sealed class ReservedArrowHotkeys : IDisposable
{
    private readonly Native.KeyboardCallback callback;
    private readonly Dictionary<uint, int> bindings;
    private readonly HashSet<uint> held = [];
    private readonly nint window;
    private nint hook;
    private static readonly Native.Input[] MenuMask =
    [
        new() { Type = 1, Data = new() { Keyboard = new() { Key = 0xE8 } } },
        new() { Type = 1, Data = new() { Keyboard = new() { Key = 0xE8, Flags = 2 } } }
    ];

    public static bool Supports(Shortcut shortcut) => shortcut.Modifiers == 9 && shortcut.Key is >= 0x25 and <= 0x28;

    public ReservedArrowHotkeys(nint window, Dictionary<uint, int> bindings)
    {
        this.window = window;
        this.bindings = bindings;
        callback = Handle;
        hook = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
        if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private nint Handle(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            uint key = (uint)Marshal.ReadInt32(data);
            // Ordinary typing exits here, without inspecting the remaining event data.
            if (bindings.TryGetValue(key, out int id))
            {
                bool down = message.ToInt32() is 0x0100 or 0x0104;
                if (!down && held.Remove(key)) return 1;
                if (down)
                {
                    uint mods = 0;
                    if (Native.GetAsyncKeyState((int)Keys.ControlKey) < 0) mods |= 2;
                    if (Native.GetAsyncKeyState((int)Keys.Menu) < 0) mods |= 1;
                    if (Native.GetAsyncKeyState((int)Keys.ShiftKey) < 0) mods |= 4;
                    if (Native.GetAsyncKeyState((int)Keys.LWin) < 0 || Native.GetAsyncKeyState((int)Keys.RWin) < 0) mods |= 8;
                    if (mods == 9)
                    {
                        if (held.Add(key))
                        {
                            // Prevent releasing Win/Alt from opening Start or an app's menu.
                            // 0xE8 is unassigned; it is never recorded or used as a shortcut.
                            Native.SendInput(2, MenuMask, Marshal.SizeOf<Native.Input>());
                            Native.PostMessage(window, Native.HotkeyMessage, id, 0);
                        }
                        return 1;
                    }
                }
            }
        }
        return Native.CallNextHookEx(hook, code, message, data);
    }

    public void Dispose()
    {
        if (hook == 0) return;
        Native.UnhookWindowsHookEx(hook);
        hook = 0;
        held.Clear();
        GC.KeepAlive(callback);
    }
}
