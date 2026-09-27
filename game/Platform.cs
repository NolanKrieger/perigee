using System.Collections.Generic;
using Godot;

namespace Perigee;

/// <summary>Achievement backend (GDD §19). The game never depends on Steam being present: without it, unlocks are kept locally in the profile.</summary>
public interface IPlatform
{
    string Name { get; }
    bool Available { get; }
    /// <summary>Unlock an achievement by its §19 id; returns true when the backend accepted it.</summary>
    bool Unlock(string id);
}

/// <summary>Offline backend: unlocks are recorded in the profile only.</summary>
public sealed class LocalPlatform : IPlatform
{
    public string Name => "local";
    public bool Available => true;
    public bool Unlock(string id) => true;
}

/// <summary>
/// Steamworks backend. Compiled only with the STEAMWORKS symbol and the Steamworks.NET assembly present; the app id comes from
/// <c>steam_appid.txt</c> next to the executable (Nolan's Steamworks account owns it — decision: config, not code).
/// Without the symbol this type still exists but reports unavailable, so the same call sites work in every build.
/// </summary>
public sealed class SteamPlatform : IPlatform
{
    public string Name => "steam";
    public bool Available { get; private set; }
    readonly HashSet<string> _sent = new();

    public SteamPlatform()
    {
#if STEAMWORKS
        try
        {
            if (Steamworks.SteamAPI.Init()) Available = true;
        }
        catch (System.Exception e) { GD.PushWarning($"Steam not available: {e.Message}"); }
#else
        Available = false;
#endif
    }

    public bool Unlock(string id)
    {
        if (!Available || !_sent.Add(id)) return false;
#if STEAMWORKS
        Steamworks.SteamUserStats.SetAchievement(id);
        Steamworks.SteamUserStats.StoreStats();
        return true;
#else
        return false;
#endif
    }
}
