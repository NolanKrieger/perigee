using System;
using System.Collections.Generic;
using System.Linq;
using Perigee.Sim;

namespace Perigee;

/// <summary>The 20 achievements of GDD §19: ids and titles are config; every condition reads world state, never memory.</summary>
public static class Achievements
{
    public sealed record Def(string Id, string Title, string How);

    public static readonly Def[] All =
    {
        new("LIFTOFF", "Liftoff", "First launch"),
        new("UP_AND_OUT", "Up and Out", "Cross the atmosphere line"),
        new("AROUND_AGAIN", "Around Again", "First orbit"),
        new("STUCK_THE_LANDING", "Stuck the Landing", "First booster recovery"),
        new("HAT_TRICK", "Hat Trick", "Recover the same design's booster 3 flights in a row"),
        new("HANDSHAKE", "Handshake", "First docking"),
        new("GAS_STATION", "Gas Station", "First depot sale"),
        new("FIRST_TOUCHDOWN", "First Touchdown", "Land on another body"),
        new("DIG_IN", "Dig In", "First outpost"),
        new("HOME_BREW", "Home Brew", "Refine your first tonne of methalox off-world"),
        new("OFF_WORLD_LIFTOFF", "Off-World Liftoff", "Launch from an off-world pad"),
        new("SKIMMER", "Skimmer", "Scoop xenon from a gas giant"),
        new("BELT_BUCKLE", "Belt Buckle", "Land on 5 asteroids"),
        new("CLEANER", "Cleaner", "Deorbit 25 debris objects"),
        new("WEATHERED_IT", "Weathered It", "Survive a storm with every craft safe"),
        new("GRAND_TOUR", "Grand Tour", "Orbit every planet in one career"),
        new("MILLIONAIRE", "Millionaire", "Net worth $10M"),
        new("BILLIONAIRE", "Billionaire", "Net worth $1B"),
        new("SHOESTRING", "Shoestring", "Reach low orbit with under $50k cash"),
        new("BRUTAL_SURVIVOR", "Brutal Survivor", "500 days on Brutal"),
    };

    static double Stat(World w, string k) => w.CareerStats.TryGetValue(k, out var v) ? v : 0;

    /// <summary>Which achievements the world state satisfies right now.</summary>
    public static IEnumerable<string> Satisfied(World w)
    {
        double recoveries = Stat(w, "recoveries");
        if (Stat(w, "launches") >= 1) yield return "LIFTOFF";
        if (w.Crafts.Any(c => c.ReachedAltitude >= w.Sys.Home.AtmoHeight) || w.Exploration.TryGetValue(w.Sys.HomeId, out var eh) && eh.HasFlag(Explored.Orbit)) yield return "UP_AND_OUT";
        if (w.Exploration.TryGetValue(w.Sys.HomeId, out var e0) && e0.HasFlag(Explored.Orbit)) yield return "AROUND_AGAIN";
        if (recoveries >= 1) yield return "STUCK_THE_LANDING";
        if (Stat(w, "booster_streak") >= 3) yield return "HAT_TRICK";
        if (Stat(w, "dockings") >= 1) yield return "HANDSHAKE";
        if (Stat(w, "depot_sales") >= 1) yield return "GAS_STATION";
        if (w.Exploration.Any(kv => kv.Key >= 0 && kv.Key != w.Sys.HomeId && kv.Value.HasFlag(Explored.Landed))) yield return "FIRST_TOUCHDOWN";
        if (w.Outposts().Any()) yield return "DIG_IN";
        if (Stat(w, "refined_methalox") >= 1) yield return "HOME_BREW";
        if (Stat(w, "pad_launches") >= 1) yield return "OFF_WORLD_LIFTOFF";
        if (Stat(w, "xenon_scooped") > 0) yield return "SKIMMER";
        if (w.Exploration.Count(kv => kv.Key >= 0 && kv.Value.HasFlag(Explored.Landed) && w.Sys[kv.Key].Type == BodyType.Asteroid) >= 5) yield return "BELT_BUCKLE";
        if (Stat(w, "debris_deorbited") >= 25) yield return "CLEANER";
        if (Stat(w, "storms_clean") >= 1) yield return "WEATHERED_IT";
        var planets = w.Sys.Bodies.Where(b => b.Parent == w.Sys.Bodies.First(x => x.Type == BodyType.Star).Id && b.Type != BodyType.Star).ToList();
        if (planets.Count > 0 && planets.All(b => w.Exploration.TryGetValue(b.Id, out var ex) && ex.HasFlag(Explored.Orbit))) yield return "GRAND_TOUR";
        long worth = w.NetWorth();
        if (worth >= 1_000_000_000) yield return "MILLIONAIRE";
        if (worth >= 100_000_000_000) yield return "BILLIONAIRE";
        if (w.Cash < 5_000_000 && w.Crafts.Any(c => !c.Destroyed && !c.IsDebris && c.BodyId == w.Sys.HomeId && c.Mode == CraftMode.Rails && c.Rails.Conic.IsEllipse && c.Rails.Conic.Periapsis >= w.Sys.Home.Radius + w.Sys.Home.AtmoHeight)) yield return "SHOESTRING";
        if (w.Preset.Name == "Brutal" && w.T >= 500 * Units.Day) yield return "BRUTAL_SURVIVOR";
    }

    public static Def? Find(string id) => Array.Find(All, a => a.Id == id);
}
