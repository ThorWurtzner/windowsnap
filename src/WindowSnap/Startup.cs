using Microsoft.Win32;

namespace WindowSnap;

internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string Command => $"\"{Environment.ProcessPath}\" --quiet";
    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(key?.GetValue("WindowSnap") as string, Command, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (value) key.SetValue("WindowSnap", Command);
            else key.DeleteValue("WindowSnap", false);
        }
    }
}
