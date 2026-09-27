namespace Perigee.Sim;

/// <summary>
/// Economy sim for the GDD §2 balance targets: with the same part catalogue and prices, compare the cheapest direct mission
/// design against a depot-assisted one (smaller launcher, fuel bought in orbit at the tanker-delivered price, upper stage
/// reused) and a mined-fuel one. Parametric rockets (engine + N tanks per stage), not the geometry builder.
/// </summary>
public static class EconomySim
{
    public sealed record StageSpec(PartDef Engine, int Engines, PartDef Tank, int Tanks)
    {
        public double DryMass => Engines * Engine.DryMass + Tanks * Tank.DryMass;
        public double Fuel => Tanks * Tank.FuelCapacity;
        public long Cost => Engines * Engine.Cost + Tanks * Tank.Cost;
        public double ThrustVac => Engines * Engine.Engine!.ThrustVac;
        public double ThrustSl => Engines * Engine.Engine!.ThrustSl;
        public override string ToString() => $"{Engines}×{Engine.Name} + {Tanks}×{Tank.Name}";
    }

    public sealed record Rocket(List<StageSpec> Stages, double Payload, long PartsCost, long FuelCost, double DeltaV, string Desc)
    {
        public long Cost => PartsCost + FuelCost;
    }

    static readonly string[] EngineIds = { "eng-kestrel", "eng-sparrow", "eng-condor", "eng-heron", "eng-titan", "eng-lander" };
    static readonly string[] TankIds = { "tank-s-short", "tank-s-long", "tank-m-short", "tank-m-medium", "tank-m-long", "tank-l-medium", "tank-l-long", "tank-l-jumbo" };
    public const double FirstStageTwr = 1.3, UpperStageTwr = 0.35, Ascent = 2600;   // Δv to low home orbit incl. losses (GDD §4)
    const double G0 = Units.G0, HomeG = 9.81;

    /// <summary>Δv of a stack: stage k burns with all stages above it (and the payload) as dead mass; vacuum Isp except the first stage (average of SL and vac).</summary>
    public static double StackDeltaV(List<StageSpec> stages, double payload, bool inSpace = false)
    {
        double dv = 0;
        for (int k = 0; k < stages.Count; k++)
        {
            double above = payload;
            for (int j = k + 1; j < stages.Count; j++) above += stages[j].DryMass + stages[j].Fuel;
            var s = stages[k];
            double m0 = above + s.DryMass + s.Fuel, m1 = above + s.DryMass;
            double isp = k == 0 && !inSpace ? 0.5 * (s.Engine.Engine!.IspSl + s.Engine.Engine.IspVac) : s.Engine.Engine!.IspVac;
            dv += isp * G0 * Math.Log(m0 / m1);
        }
        return dv;
    }

    static bool TwrOk(List<StageSpec> stages, double payload, bool inSpace)
    {
        for (int k = 0; k < stages.Count; k++)
        {
            double mass = payload;
            for (int j = k; j < stages.Count; j++) mass += stages[j].DryMass + stages[j].Fuel;
            double thrust = k == 0 && !inSpace ? stages[k].ThrustSl : stages[k].ThrustVac;
            double twr = thrust / (mass * 1000 * HomeG);
            if (twr < (k == 0 && !inSpace ? FirstStageTwr : UpperStageTwr)) return false;
        }
        return true;
    }

    /// <summary>Cheapest rocket (parts + home fuel) that gives `payload` tonnes at least `dv` m/s, 1–3 stages. Engines/tanks from the catalogue.</summary>
    public static Rocket? Cheapest(double payload, double dv, int maxStages = 3, bool firstStageMustBeSeaLevel = true, bool inSpace = false)
    {
        Rocket? best = null;
        var engines = EngineIds.Select(Parts.Get).ToList();
        var tanks = TankIds.Select(Parts.Get).ToList();
        var stageOptions = new List<StageSpec>();
        foreach (var e in engines) foreach (var t in tanks)
        {
            if (t.Dia != e.Dia && !(e.Dia == Diameter.S && t.Dia == Diameter.S)) { if (t.Dia < e.Dia) continue; }
            for (int n = 1; n <= 6; n++) for (int ne = 1; ne <= (e.Dia == Diameter.S ? 3 : 2); ne++) stageOptions.Add(new StageSpec(e, ne, t, n));
        }
        void Consider(List<StageSpec> stages)
        {
            if (firstStageMustBeSeaLevel && stages[0].Engine.Engine!.ThrustSl < 0.5 * stages[0].Engine.Engine.ThrustVac) return;
            if (!TwrOk(stages, payload, inSpace)) return;
            double d = StackDeltaV(stages, payload, inSpace);
            if (d < dv) return;
            long parts = stages.Sum(s => s.Cost);
            long fuel = (long)(stages.Sum(s => s.Fuel) * Market.BasePrice(Resource.Methalox));
            if (best == null || parts + fuel < best.Cost) best = new Rocket(new List<StageSpec>(stages), payload, parts, fuel, d, string.Join(" / ", stages.Select(s => s.ToString())));
        }
        foreach (var s1 in stageOptions)
        {
            Consider(new List<StageSpec> { s1 });
            if (maxStages < 2) continue;
            foreach (var s2 in stageOptions)
            {
                if (s2.Tank.Dia > s1.Tank.Dia) continue;   // upper stages no wider than the one below
                var two = new List<StageSpec> { s1, s2 };
                if (StackDeltaV(two, payload, inSpace) < dv * 0.6 && maxStages < 3) continue;
                Consider(two);
            }
        }
        return best;
    }

    /// <summary>Recovery kit for a stage that comes home (GDD §6): grid fins + legs for a booster, a shield + chute for an orbital stage.</summary>
    public static long RecoveryKit(StageSpec st, bool fromOrbit)
    {
        string legs = st.Tank.Dia == Diameter.S ? "legs-s" : st.Tank.Dia == Diameter.M ? "legs-m" : "legs-l";
        string shield = st.Tank.Dia == Diameter.S ? "shield-s" : st.Tank.Dia == Diameter.M ? "shield-m" : "shield-l";
        return fromOrbit ? Parts.Get(shield).Cost + Parts.Get("chute-radial").Cost * 2 + Parts.Get(legs).Cost * 2 : 2 * Parts.Get("grid-fin").Cost + 2 * Parts.Get(legs).Cost;
    }

    /// <summary>Net cost once refunds are counted: the first `recovered` stages come home for 90% (they need their kits); the rest are expended.</summary>
    public static long NetCost(Rocket r, int recovered)
    {
        long net = r.Cost;
        for (int k = 0; k < Math.Min(recovered, r.Stages.Count); k++)
        {
            var st = r.Stages[k];
            long kit = RecoveryKit(st, fromOrbit: k > 0 || r.Stages.Count == 1);
            net += kit - (long)(0.9 * (st.Cost + kit));
        }
        return net;
    }

    /// <summary>A direct mission recovers only its booster (when there is one above it to leave behind).</summary>
    public static long NetCostWithBoosterReuse(Rocket r) => NetCost(r, r.Stages.Count >= 2 ? 1 : 0);

    /// <summary>Cents per tonne of methalox delivered to low home orbit by the cheapest reusable tanker (payload = tank + fuel; tank recovered 90%).</summary>
    public static (long pricePerTonne, string desc) DeliveredFuelPrice()
    {
        (long price, string desc) best = (long.MaxValue, "");
        foreach (var tankId in TankIds)
        {
            var tank = Parts.Get(tankId);
            for (int n = 1; n <= 4; n++)
            {
                double fuel = n * tank.FuelCapacity;
                double payload = n * (tank.DryMass + tank.FuelCapacity) + 0.3;   // + a small core/port to dock
                var r = Cheapest(payload, Ascent + 150);
                if (r == null) continue;
                long net = NetCost(r, r.Stages.Count) + (long)(fuel * Market.BasePrice(Resource.Methalox)) + n * tank.Cost + 800_000;
                // The tanker's own tank comes home for 90%: refund it.
                net -= (long)(0.9 * (n * tank.Cost + 800_000));
                long per = (long)(net / fuel);
                if (per < best.price) best = (per, $"{n}×{tank.Name} on {r.Desc}");
            }
        }
        return best;
    }

    public sealed record Comparison(string Mission, double MissionDv, long Direct, string DirectDesc, long Depot, string DepotDesc, long Mined, long FuelPriceInOrbit)
    {
        public double DepotRatio => Depot / (double)Direct;
        public double MinedRatio => Mined / (double)Direct;
    }

    /// <summary>
    /// GDD §2: land `payload` t on `body`. Direct = one launch does everything; only the booster comes home (the stage that
    /// carried the mission fuel up is thrown away). Depot = a fully reusable launcher (booster + orbital stage, both recovered)
    /// lifts the mission stack with empty tanks; the stack (transfer stage + lander) buys its fuel at the depot at the
    /// tanker-delivered price and flies one way, expended. Mined = the same with fuel at the mined price.
    /// </summary>
    public static Comparison Compare(StarSystem sys, Body body, double payload)
    {
        double toLow = DeltaV.LowToLow(sys, sys.HomeId, body.Id);
        double land = DeltaV.LandingFromLowOrbit(body);
        double missionDv = toLow + land;
        var direct = Cheapest(payload, Ascent + missionDv)!;
        long directCost = NetCostWithBoosterReuse(direct);

        var lander = Cheapest(payload, land + 50, 1, firstStageMustBeSeaLevel: false, inSpace: true)!;
        var landerStage = lander.Stages[0];
        double transferPayload = payload + landerStage.DryMass + landerStage.Fuel;
        var transfer = Cheapest(transferPayload, toLow + 80, 1, firstStageMustBeSeaLevel: false, inSpace: true)!;
        var transferStage = transfer.Stages[0];
        double stackDry = payload + landerStage.DryMass + transferStage.DryMass;   // launched with empty tanks
        var launcher = Cheapest(stackDry, Ascent + 150)!;
        var (fuelPrice, fuelDesc) = DeliveredFuelPrice();
        double orbitalFuel = transferStage.Fuel + landerStage.Fuel;
        long launcherNet = NetCost(launcher, launcher.Stages.Count);   // every launcher stage comes home
        long depotCost = launcherNet + landerStage.Cost + transferStage.Cost + (long)(orbitalFuel * fuelPrice);
        long minedCost = launcherNet + landerStage.Cost + transferStage.Cost + (long)(orbitalFuel * fuelPrice * 0.35);
        return new Comparison(body.Name, missionDv, directCost, direct.Desc, depotCost, $"{launcher.Desc} (all recovered) lifting {transferStage} + lander {landerStage}, {orbitalFuel:0.0} t bought in orbit ({fuelDesc})", minedCost, fuelPrice);
    }
}
