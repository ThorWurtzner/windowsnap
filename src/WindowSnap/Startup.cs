using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;

namespace WindowSnap;

internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string Arguments = "--quiet --startup";
    private static string Executable => Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
    internal static string TaskName => "WindowSnap-" + UserId;
    private static string LegacyCommand => $"\"{Executable}\" --quiet";
    private static string UserId
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? throw new InvalidOperationException("Current user unavailable.");
        }
    }

    public static bool Enabled
    {
        get
        {
            using var scope = new ComScope();
            dynamic folder = OpenFolder(scope);
            object? found = FindTask(folder, TaskName);
            if (found is not null)
            {
                dynamic task = scope.Track(found);
                return (bool)task.Enabled && MatchesRegistration((string)task.Xml, Executable, UserId);
            }
            // Retain the existing preference until it is migrated to a scheduled task.
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(key?.GetValue("WindowSnap") as string, LegacyCommand, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using var scope = new ComScope();
            dynamic folder = OpenFolder(scope);
            if (value)
            {
                if (string.Equals(Path.GetFileName(Executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Run WindowSnap.exe to configure startup.");
                string user = UserId;
                // Interactive token, least privilege, current user only; no password is stored.
                dynamic registered = scope.Track((object)folder.RegisterTask(TaskName,
                    BuildTaskXml(Executable, AppContext.BaseDirectory, user), 6, user, null, 3, null));
                if (!(bool)registered.Enabled || !MatchesRegistration((string)registered.Xml, Executable, user))
                    throw new InvalidOperationException("Startup task verification failed.");
            }
            else
            {
                try { folder.DeleteTask(TaskName, 0); }
                catch (Exception ex) when (IsMissing(ex)) { }
            }
            // Remove the legacy entry only after the new registration is verified.
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue("WindowSnap", false);
        }
    }

    internal static string BuildTaskXml(string executable, string directory, string user)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        return new XDocument(new XElement(ns + "Task", new XAttribute("version", "1.4"),
            new XElement(ns + "RegistrationInfo", new XElement(ns + "Description", "WindowSnap at sign-in")),
            new XElement(ns + "Triggers", new XElement(ns + "LogonTrigger",
                new XElement(ns + "Enabled", true), new XElement(ns + "UserId", user), new XElement(ns + "Delay", "PT10S"))),
            new XElement(ns + "Principals", new XElement(ns + "Principal", new XAttribute("id", "WindowSnap"),
                new XElement(ns + "UserId", user), new XElement(ns + "LogonType", "InteractiveToken"),
                new XElement(ns + "RunLevel", "LeastPrivilege"))),
            new XElement(ns + "Settings",
                new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", false), new XElement(ns + "StopIfGoingOnBatteries", false),
                new XElement(ns + "StartWhenAvailable", true), new XElement(ns + "AllowStartOnDemand", true),
                new XElement(ns + "Enabled", true), new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                new XElement(ns + "Priority", 4),
                new XElement(ns + "RestartOnFailure", new XElement(ns + "Interval", "PT1M"), new XElement(ns + "Count", 3))),
            new XElement(ns + "Actions", new XAttribute("Context", "WindowSnap"), new XElement(ns + "Exec",
                new XElement(ns + "Command", executable), new XElement(ns + "Arguments", Arguments),
                new XElement(ns + "WorkingDirectory", directory))))).ToString();
    }

    internal static bool MatchesRegistration(string xml, string executable, string user)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var document = XDocument.Parse(xml);
        var action = document.Root?.Element(ns + "Actions")?.Element(ns + "Exec");
        return string.Equals(action?.Element(ns + "Command")?.Value, executable, StringComparison.OrdinalIgnoreCase) &&
            action?.Element(ns + "Arguments")?.Value == Arguments &&
            document.Descendants(ns + "LogonTrigger").Any(trigger => SameUser(trigger.Element(ns + "UserId")?.Value, user) &&
                trigger.Element(ns + "Enabled")?.Value != "false") &&
            document.Descendants(ns + "Principal").Any(principal => SameUser(principal.Element(ns + "UserId")?.Value, user) &&
                principal.Element(ns + "LogonType")?.Value == "InteractiveToken" &&
                principal.Element(ns + "RunLevel")?.Value is null or "LeastPrivilege");
    }

    private static bool SameUser(string? account, string sid)
    {
        if (string.Equals(account, sid, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrWhiteSpace(account) || account.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)) return false;
        // Windows normalizes logon trigger SIDs to account names, and omits default values.
        using var current = WindowsIdentity.GetCurrent();
        if (current.User?.Value == sid && string.Equals(current.Name, account, StringComparison.OrdinalIgnoreCase)) return true;
        try { return ((SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier))).Value == sid; }
        catch (IdentityNotMappedException) { return false; }
    }

    private static dynamic OpenFolder(ComScope scope)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Task Scheduler unavailable.");
        dynamic service = scope.Track(Activator.CreateInstance(type)!);
        service.Connect();
        return scope.Track((object)service.GetFolder("\\"));
    }

    private static object? FindTask(dynamic folder, string name)
    {
        try { return folder.GetTask(name); }
        catch (Exception ex) when (IsMissing(ex)) { return null; }
    }

    private static bool IsMissing(Exception ex) => (uint)ex.HResult is 0x80070002 or 0x80070003;

    private sealed class ComScope : IDisposable
    {
        private readonly List<object> objects = [];
        public dynamic Track(object value) { objects.Add(value); return value; }
        public void Dispose()
        {
            for (int index = objects.Count - 1; index >= 0; index--)
                if (Marshal.IsComObject(objects[index])) Marshal.ReleaseComObject(objects[index]);
        }
    }
}
