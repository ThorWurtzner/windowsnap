using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowSnap;

public enum ActionKind { Position, Maximize, Undo, PreviousMonitor, NextMonitor, Center }

public sealed class Binding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Custom position";
    public ActionKind Kind { get; set; }
    public string Shortcut { get; set; } = "";
    public List<Region> Regions { get; set; } = [];
    public bool Custom { get; set; }
}

public sealed class Configuration
{
    public int Version { get; set; } = 1;
    public int Gap { get; set; }
    public bool CycleSizes { get; set; } = true;
    public List<Binding> Bindings { get; set; } = [];

    public static Configuration Defaults()
    {
        var config = new Configuration();
        void Add(string id, string name, string key, params Region[] regions) => config.Bindings.Add(new()
        { Id = id, Name = name, Shortcut = key, Kind = ActionKind.Position, Regions = [.. regions] });
        void Command(string id, string name, string key, ActionKind kind) => config.Bindings.Add(new()
        { Id = id, Name = name, Shortcut = key, Kind = kind });
        Add("left", "Left half · ⅔ · ⅓ · ¾", "Ctrl+Alt+Left", new(0, 0, .5, 1), new(0, 0, 2d / 3, 1), new(0, 0, 1d / 3, 1), new(0, 0, .75, 1));
        Add("right", "Right half · ⅔ · ⅓ · ¾", "Ctrl+Alt+Right", new(.5, 0, .5, 1), new(1d / 3, 0, 2d / 3, 1), new(2d / 3, 0, 1d / 3, 1), new(.25, 0, .75, 1));
        Command("maximize", "Maximize", "Ctrl+Alt+Up", ActionKind.Maximize);
        Command("undo", "Undo last move", "Ctrl+Alt+Down", ActionKind.Undo);
        Add("left-third", "Left third", "Ctrl+Alt+D1", new Region(0, 0, 1d / 3, 1));
        Add("middle-third", "Middle third", "Ctrl+Alt+D2", new Region(1d / 3, 0, 1d / 3, 1));
        Add("right-third", "Right third", "Ctrl+Alt+D3", new Region(2d / 3, 0, 1d / 3, 1));
        Add("left-two-thirds", "Left two-thirds", "Ctrl+Alt+D4", new Region(0, 0, 2d / 3, 1));
        Add("right-two-thirds", "Right two-thirds", "Ctrl+Alt+D5", new Region(1d / 3, 0, 2d / 3, 1));
        Add("left-three-quarters", "Left three-quarters", "Ctrl+Alt+D6", new Region(0, 0, .75, 1));
        Add("right-three-quarters", "Right three-quarters", "Ctrl+Alt+D7", new Region(.25, 0, .75, 1));
        Add("center-three-quarters", "Centered three-quarters", "Ctrl+Alt+D8", new Region(.125, 0, .75, 1));
        Add("center-half", "Centered half", "Ctrl+Alt+C", new Region(.25, 0, .5, 1));
        Add("center-large", "Centered 75% × 85%", "Ctrl+Alt+Return", new Region(.125, .075, .75, .85));
        Command("center", "Center, keep size", "Ctrl+Alt+Space", ActionKind.Center);
        Add("top", "Top half", "Ctrl+Alt+U", new Region(0, 0, 1, .5));
        Add("bottom", "Bottom half", "Ctrl+Alt+J", new Region(0, .5, 1, .5));
        Add("top-left", "Top-left quarter", "Ctrl+Alt+Home", new Region(0, 0, .5, .5));
        Add("top-right", "Top-right quarter", "Ctrl+Alt+PageUp", new Region(.5, 0, .5, .5));
        Add("bottom-left", "Bottom-left quarter", "Ctrl+Alt+End", new Region(0, .5, .5, .5));
        Add("bottom-right", "Bottom-right quarter", "Ctrl+Alt+PageDown", new Region(.5, .5, .5, .5));
        Command("previous-monitor", "Previous monitor (left)", "Ctrl+Alt+Shift+Left", ActionKind.PreviousMonitor);
        Command("next-monitor", "Next monitor (right)", "Ctrl+Alt+Shift+Right", ActionKind.NextMonitor);
        // Win+Alt matches the user's preferred modifier pair and avoids AltGr typing.
        foreach (var binding in config.Bindings)
        {
            binding.Shortcut = binding.Shortcut.Replace("Ctrl+Alt+", "Win+Alt+");
            if (binding.Shortcut.Split('+')[^1] is { Length: 2 } key && key[0] == 'D')
                binding.Shortcut = binding.Shortcut.Replace("Win+Alt+", "Win+Alt+Shift+");
        }
        return config;
    }

    public Configuration Clone() => JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public void Validate()
    {
        if (Version != 1 || Gap is < 0 or > 64 || Bindings is null || Bindings.Count > 100)
            throw new InvalidDataException("Unsupported or invalid settings.");
        var ids = new HashSet<string>();
        foreach (var binding in Bindings)
        {
            if (binding is null || string.IsNullOrWhiteSpace(binding.Id) || !ids.Add(binding.Id) ||
                string.IsNullOrWhiteSpace(binding.Name) || binding.Name.Length > 100 || !Enum.IsDefined(binding.Kind) ||
                binding.Shortcut is null || binding.Regions is null || binding.Regions.Count > 16 ||
                (binding.Kind == ActionKind.Position && (binding.Regions.Count == 0 || binding.Regions.Any(r => !r.IsValid))))
                throw new InvalidDataException("A position or action in settings is invalid.");
        }
    }
}

public sealed class SettingsStore(string directory)
{
    public string FilePath { get; } = Path.Combine(directory, "settings.json");
    public string? Warning { get; private set; }

    public Configuration Load()
    {
        if (!File.Exists(FilePath)) return Configuration.Defaults();
        try
        {
            var result = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(FilePath), Configuration.JsonOptions)
                ?? throw new InvalidDataException("Settings are empty.");
            result.Validate();
            return result;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            Warning = "Settings unreadable; defaults loaded. Original file retained. " + ex.Message;
            return Configuration.Defaults();
        }
    }

    public void Save(Configuration config)
    {
        config.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Configuration.JsonOptions));
        File.Move(temp, FilePath, true);
        Warning = null;
    }
}
