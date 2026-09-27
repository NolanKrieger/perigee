using Godot;
using Perigee.Sim;

namespace Perigee;

/// <summary>Career save files: one autosave per career at user://careers/&lt;slug&gt;.pfc (GDD §18), with autosave triggers driven by the sim's SaveRequested flag,
/// a 3-real-minute timer and quit.</summary>
public static class Careers
{
    public static string Dir => ProjectSettings.GlobalizePath(Main.Args.TryGetValue("careers", out var d) ? d : "user://careers");
    public const double AutosaveInterval = 180;   // real seconds (GDD §18)
    public const double MinGap = 5;               // real seconds between trigger-driven saves

    public static string Slug(string agency)
    {
        var s = new string(agency.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        return s.Length == 0 ? "career" : s;
    }

    public static string PathFor(string agency) => System.IO.Path.Combine(Dir, Slug(agency) + ".pfc");

    public static List<(string path, SaveData data)> List()
    {
        var result = new List<(string, SaveData)>();
        if (!Directory.Exists(Dir)) return result;
        foreach (var f in Directory.GetFiles(Dir, "*.pfc").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try { result.Add((f, SaveStore.ReadOrBackup(f))); }
            catch (Exception e) { GD.PushWarning($"unreadable save {f}: {e.Message}"); }
        }
        return result;
    }

    public static void Save(World w)
    {
        if (w.InFlashback) return;
        SaveStore.Write(PathFor(w.AgencyName), w.ToSave());
        w.SaveRequested = false;
        Profile.Data.LastCareer = w.AgencyName; Profile.Save();
    }

    public static World Load(string path) => World.FromSave(SaveStore.ReadOrBackup(path));

    public static void Delete(string path)
    {
        foreach (var p in new[] { path, path + ".bak", path + ".tmp" }) if (File.Exists(p)) File.Delete(p);
    }
}
