using Godot;
using System.Text.Json;

namespace Perigee;

/// <summary>Player profile (not a save): settings and personal bests per preset, at user://profile.json (atomic write).</summary>
public sealed class ProfileData
{
    public int Version { get; set; } = 1;
    public double MasterVolume { get; set; } = 0.8;
    public double MusicVolume { get; set; } = 0.7;
    public double SfxVolume { get; set; } = 0.8;
    public double UiVolume { get; set; } = 0.8;
    public bool Fullscreen { get; set; } = false;
    public int ResolutionIndex { get; set; } = 1;
    public double UiScale { get; set; } = 1.0;
    public Dictionary<string, List<int>> Bindings { get; set; } = new();     // action → physical keycodes
    public Dictionary<string, BestData> Bests { get; set; } = new();          // preset → best
    public List<string> Achievements { get; set; } = new();                  // unlocked §19 ids (also sent to the platform backend)
    public string LastCareer { get; set; } = "";
}

public sealed class BestData { public long PeakScore { get; set; } public int Days { get; set; } public string Agency { get; set; } = ""; public string When { get; set; } = ""; }

public static class Profile
{
    public static readonly (int w, int h)[] Resolutions = { (1280, 720), (1600, 900), (1920, 1080), (2560, 1440) };
    public static readonly string[] Buses = { "Master", "Music", "SFX", "UI" };
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static ProfileData Data { get; private set; } = new();
    public static string Path => ProjectSettings.GlobalizePath(Main.Args.TryGetValue("profile", out var p) && p.Length > 0 ? p : "user://profile.json");

    public static void Load()
    {
        try { if (File.Exists(Path)) Data = JsonSerializer.Deserialize<ProfileData>(File.ReadAllText(Path), Options) ?? new ProfileData(); }
        catch (Exception e) { GD.PushWarning($"profile unreadable, using defaults: {e.Message}"); Data = new ProfileData(); }
        EnsureBuses();
        Apply();
    }

    public static void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(dir);
        string tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Options));
        File.Move(tmp, Path, overwrite: true);
    }

    /// <summary>Audio buses exist from the start so the volume settings have somewhere to land (the M13 audio pass plays through them).</summary>
    /// <summary>Records an unlock; true when it was new.</summary>
    public static bool UnlockAchievement(string id)
    {
        if (Data.Achievements.Contains(id)) return false;
        Data.Achievements.Add(id); Save();
        return true;
    }

    public static void EnsureBuses()
    {
        foreach (var name in Buses)
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            AudioServer.AddBus();
            int idx = AudioServer.BusCount - 1;
            AudioServer.SetBusName(idx, name);
            AudioServer.SetBusSend(idx, "Master");
        }
    }

    public static void Apply()
    {
        SetBus("Master", Data.MasterVolume); SetBus("Music", Data.MusicVolume); SetBus("SFX", Data.SfxVolume); SetBus("UI", Data.UiVolume);
        if (!Main.Args.ContainsKey("screenshot") && !Main.Args.ContainsKey("shots") && !Main.Args.ContainsKey("selftest"))
        {
            DisplayServer.WindowSetMode(Data.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
            if (!Data.Fullscreen)
            {
                var (w, h) = Resolutions[Math.Clamp(Data.ResolutionIndex, 0, Resolutions.Length - 1)];
                DisplayServer.WindowSetSize(new Vector2I(w, h));
            }
        }
        var root = (SceneTree)Engine.GetMainLoop();
        root.Root.ContentScaleFactor = (float)Math.Clamp(Data.UiScale, 0.75, 1.5);
        ApplyBindings();
    }

    static void SetBus(string name, double v)
    {
        int idx = AudioServer.GetBusIndex(name);
        if (idx < 0) return;
        AudioServer.SetBusVolumeDb(idx, v <= 0.001 ? -80 : (float)Mathf.LinearToDb((float)v));
        AudioServer.SetBusMute(idx, v <= 0.001);
    }

    public static void ApplyBindings()
    {
        foreach (var (action, keys) in Data.Bindings)
        {
            if (!InputMap.HasAction(action)) continue;
            InputMap.ActionEraseEvents(action);
            foreach (var k in keys) InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = (Key)k });
        }
    }

    public static void Rebind(string action, Key key)
    {
        Data.Bindings[action] = new List<int> { (int)key };
        ApplyBindings();
        Save();
    }

    public static void ResetBindings()
    {
        Data.Bindings.Clear();
        foreach (var (action, _) in Controls.All) if (InputMap.HasAction(action)) InputMap.ActionEraseEvents(action);
        Controls.Ensure();
        Save();
    }

    public static string KeyLabel(string action)
    {
        if (!InputMap.HasAction(action)) return "—";
        var ev = InputMap.ActionGetEvents(action).OfType<InputEventKey>().FirstOrDefault();
        return ev == null ? "—" : OS.GetKeycodeString(ev.PhysicalKeycode);
    }

    /// <summary>Record a personal best per preset (GDD §3: peak score, shown on the title screen).</summary>
    public static bool RecordBest(string preset, long peakScore, int days, string agency)
    {
        if (Data.Bests.TryGetValue(preset, out var b) && b.PeakScore >= peakScore) return false;
        Data.Bests[preset] = new BestData { PeakScore = peakScore, Days = days, Agency = agency, When = DateTime.Now.ToString("yyyy-MM-dd") };
        Save();
        return true;
    }
}
