using System.Runtime.InteropServices;

namespace WindowSnap;

internal readonly record struct Shortcut(uint Modifiers, uint Key)
{
    public static Shortcut Parse(string text)
    {
        uint modifiers = 0;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new FormatException("Modifier and key required.");
        foreach (string part in parts[..^1])
            modifiers |= part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => 2u, "alt" => 1u, "shift" => 4u, "win" => 8u,
                _ => throw new FormatException($"Unknown modifier: {part}.")
            };
        string keyName = parts[^1];
        if (keyName.Length == 1 && char.IsAsciiDigit(keyName[0])) keyName = "D" + keyName;
        if (!Enum.TryParse<Keys>(keyName, true, out var key) || (uint)key is < 0x08 or > 0xFE ||
            key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.F12)
            throw new FormatException("Unsupported key; F12 is reserved.");
        if ((modifiers & (1 | 2 | 8)) == 0) throw new FormatException("Ctrl, Alt, or Win required.");
        return new(modifiers, (uint)key);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & 8) != 0) parts.Add("Win");
        if ((Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((Modifiers & 1) != 0) parts.Add("Alt");
        if ((Modifiers & 4) != 0) parts.Add("Shift");
        parts.Add(((Keys)Key).ToString());
        return string.Join('+', parts);
    }

    public string Display
    {
        get
        {
            string text = ToString();
            string name = ((Keys)Key).ToString();
            string display = name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]) ? name[1..] :
                name switch { "Return" => "Enter", "PageUp" => "PgUp", "PageDown" => "PgDn", _ => name };
            return text[..^(name.Length)] + display;
        }
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly Dictionary<int, Binding> registered = [];
    private ReservedArrowHotkeys? reservedArrows;
    public Action<Binding, nint>? Pressed { get; set; }
    public Action<bool>? InstanceMessage { get; set; }
    public HotkeyWindow() => CreateHandle(new CreateParams { Caption = "WindowSnap.Hotkeys", Parent = new nint(-3) });

    public List<string> Register(Configuration config)
    {
        Clear();
        var failures = new List<string>();
        var seen = new HashSet<Shortcut>();
        var arrows = new Dictionary<uint, int>();
        for (int i = 0; i < config.Bindings.Count; i++)
        {
            var binding = config.Bindings[i];
            if (string.IsNullOrWhiteSpace(binding.Shortcut)) continue;
            try
            {
                var shortcut = Shortcut.Parse(binding.Shortcut);
                if (!seen.Add(shortcut)) { failures.Add($"{binding.Name}: duplicate shortcut {shortcut.Display}."); continue; }
                if (Native.RegisterHotKey(Handle, i + 1, shortcut.Modifiers | Native.NoRepeat, shortcut.Key)) registered.Add(i + 1, binding);
                else if (Marshal.GetLastWin32Error() == 1409 && ReservedArrowHotkeys.Supports(shortcut))
                { arrows.Add(shortcut.Key, i + 1); registered.Add(i + 1, binding); }
                else failures.Add($"{binding.Name}: {shortcut.Display} is unavailable (Windows error {Marshal.GetLastWin32Error()}).");
            }
            catch (FormatException ex) { failures.Add($"{binding.Name}: {ex.Message}"); }
        }
        if (arrows.Count > 0)
        {
            try { reservedArrows = new(Handle, arrows); }
            catch (System.ComponentModel.Win32Exception ex)
            {
                foreach (int id in arrows.Values) registered.Remove(id);
                failures.Add("Windows blocked Win+Alt+arrow shortcuts: " + ex.Message);
            }
        }
        return failures;
    }

    public List<string> ValidateAvailable(Configuration candidate, Configuration current)
    {
        var failures = new List<string>();
        var seen = new HashSet<Shortcut>();
        // Release our own bindings temporarily so every new assignment can be checked.
        Clear();
        try
        {
            foreach (var binding in candidate.Bindings)
            {
                if (string.IsNullOrWhiteSpace(binding.Shortcut)) continue;
                try
                {
                    var shortcut = Shortcut.Parse(binding.Shortcut);
                    if (!seen.Add(shortcut)) { failures.Add($"{binding.Name}: duplicate shortcut {shortcut.Display}."); continue; }
                    if (Native.RegisterHotKey(Handle, 1000, shortcut.Modifiers | Native.NoRepeat, shortcut.Key)) Native.UnregisterHotKey(Handle, 1000);
                    else if (Marshal.GetLastWin32Error() != 1409 || !ReservedArrowHotkeys.Supports(shortcut))
                        failures.Add($"{binding.Name}: {shortcut.Display} is used by another app or Windows.");
                }
                catch (FormatException ex) { failures.Add($"{binding.Name}: {ex.Message}"); }
            }
        }
        finally { Register(current); }
        return failures;
    }

    public void Clear()
    {
        reservedArrows?.Dispose();
        reservedArrows = null;
        foreach (int id in registered.Keys) Native.UnregisterHotKey(Handle, id);
        registered.Clear();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg is 0x8001 or 0x8002) InstanceMessage?.Invoke(message.Msg == 0x8002);
        if (message.Msg == Native.HotkeyMessage && registered.TryGetValue(message.WParam.ToInt32(), out var binding))
            Pressed?.Invoke(binding, Native.GetForegroundWindow());
        base.WndProc(ref message);
    }

    public void Dispose() { Clear(); DestroyHandle(); }
}
