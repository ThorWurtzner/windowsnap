namespace WindowSnap;

internal static class StartupLog
{
    private static readonly object Gate = new();
    private static string? file;

    public static void Initialize(string directory) => file = Path.Combine(directory, "startup.log");

    public static void Write(string message, Exception? exception = null)
    {
        // Lifecycle events only. Never record keystrokes, window titles, or settings.
        if (file is null) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                if (File.Exists(file) && new FileInfo(file).Length > 64 * 1024)
                    File.Move(file, file + ".old", true);
                File.AppendAllText(file, $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} {message}{Environment.NewLine}" +
                    (exception is null ? "" : exception + Environment.NewLine));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { /* Logging must not prevent startup or shutdown. */ }
    }
}
