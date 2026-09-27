# BALANCE — tuned numbers and why

*Every GDD value tagged (proposal) that gets changed, with the reason. GDD decisions (§22) are never changed here.*

| Date | Value | GDD proposal | Now | Why |
|---|---|---|---|---|
| 2026-09-23 | Active zone above airless terrain | (not specified) | highest terrain + 2 km | A coasting craft leaves the rails and gets real physics before it can meet terrain. |
| 2026-09-23 | "Low orbit" radius for Δv estimates | (not specified) | radius × 1.1 + atmosphere height | Home: 95 km, inside the GDD's 60–120 km band. |
| 2026-09-23 | Reentry heat constant K (flux = K·√ρ·v³) | "Sutton–Graves form" | 3.2e-3 with 15% skin mass fraction (40 kg floor), heating capped at a stagnation temperature 290 K + v²/1000 (cp halved vs air) | Real k (1.7e-4) with 2 km/s orbits gives harmless entries (v³ is 64× lower than Earth's). Scaled so a bare probe entering from low orbit burns up in ~25 s and a shielded one holds at 1,400 K on its ablator. |
| 2026-09-23 | Landing zone radius (90% refund) | "your home pad or landing zone" | 5 km around the launch site | Boostback precision of a hand-flown booster; beyond that the 50% "other dry land" rate applies. |
| 2026-09-23 | Drone-ship deck reach | (not specified) | 40 m from its centre | 60 m deck drawn; a chute or engine landing within it counts. |
| 2026-09-23 | Generator Δv budgets | ice+ore ≈2,500 m/s; CO₂ ~5 km/s | 2,500 / 5,000 / every body ≤16,000 m/s (low orbit to low orbit) | Outer planets capped at 40× home's orbit radius so the far system stays reachable with depots. |
| 2026-09-23 | Engine restarts | Condor "restartable" | every engine restarts (v1) | The deorbit burn in the tutorial needs the Sparrow to relight; Condor's edge is its 15% deep throttle. |
| 2026-09-23 | Vacuum engine prices (Sparrow / Heron / Lander) | not anchored in §10 | $24k / $90k / $32k (were $9k / $38k / $14k) | GDD §2 point 2: expendable upper stages are "the biggest cost of a launch". With cheap vacuum engines the economy sim gave depots only a 27% saving on a moon mission; pricing the upper-stage engines like hardware you'd rather bring home is what makes refuelling pay. Anchored engines (Kestrel $12k, Condor $55k, Titan $180k) unchanged. |

| 2026-09-23 | Tutorial rewards T1–T10 | $40k … $600k (§11) | as the GDD table, × preset `ContractPay` | Unchanged; the advance is 20% and the failure penalty repays it plus 10% of the reward. |
| 2026-09-23 | Offer lifetime / daily refresh | "3–6 open offers, refreshed daily" | offers expire after 5 days; each day adds 1–3 up to 6; the board never drops below 3; tutorial offers never expire | Keeps the board moving without flooding it. |
| 2026-09-23 | Contract deadlines | (not specified) | tutorials 40 days; procedural 25–90 days from acceptance | Long enough for a transfer + return at the decided scale (home→moon Hohmann ≈ 10 h). |
| 2026-09-23 | Procedural rewards | (not specified) | base per type × (1 + Δv(home→body)/2,500); fuel delivery pays 1.6× the delivered fuel's orbital value | Pulls outward: a Vell contract pays ~1.5–2× a home one. |
| 2026-09-23 | Client craft orbits | "real NPC craft on rails" | tutorial clients at 120 km (home); procedural at active zone + 70–130 km, random phase | Above the atmosphere, inside the low-orbit market band. |
| 2026-09-23 | Deploy windows | "an orbit window" | min = active zone + 20 km … 0.7 × band top (10 km steps), width 60–150 km | Reachable from the tutorial rockets; never overlaps the atmosphere. |

| 2026-09-23 | Drill power / refinery power | (not specified) | 3 kW per drill; Refinery S 20 kW, Refinery L 80 kW | A 4-panel outpost at home distance (5 kW) runs one drill fully; refineries want RTGs or many panels, which is the §9 "power-hungry" trade. |
| 2026-09-23 | Solar night average | "night on a rotating body is averaged" | ×0.5 everywhere on the ground | Simple, and tidally locked moons still see a day/night cycle each orbit. |
| 2026-09-23 | Ore → metal | (not specified) | 0.6 t metal per t ore | Keeps metal for off-world parts (1.5× part mass) a real mining job. |
| 2026-09-23 | Scoop yield | "small amounts per pass" | 0.001 t/s at ρ = 3e-11 kg/m³ and 20 km/s, linear in both (≈0.1 t per pass at the top of a giant's atmosphere) | Real mass flow at survivable densities is grams; this is the gamey value that makes a pass worth ~$4k of xenon. |
| 2026-09-23 | Giant atmosphere tops | 220 km / 160 km (generator) | 690 km / 535 km (gas / ice giant), same scale heights | The old tops had ρ ≈ 2e-4 at the rails boundary: any dip at 20+ km/s melted the scoop instantly. Now ρ ≈ 1e-11 at the top, so a skim survives and a deeper dip is the risk. |

| 2026-09-23 | Off-world pad limits | (not specified) | 30 m tall, 250 t | Smaller than the home pad (40 m / 400 t): a kit, not a launch complex. |
| 2026-09-23 | Pad kit levelling | "land a craft on flat ground" | 120 m of level ground around the kit, blended over another 120 m; launch spot 30 m downrange | Natural terrain slopes made a freshly built rocket alternate between lift-off and touchdown without rising; a kit is a levelled platform. |
| 2026-09-23 | Off-world recovery | 90%, metal share to storage, rest in cash | 90% of the cash share paid; 90% of the metal share and all unburnt propellant back into the outpost's stores (lost if there is no room) | Propellant "refunded at the local price" would let a pad mint cash from its own free fuel; returning the tonnes keeps it physical. |

| 2026-09-23 | Debris decay | "slowly sinks and reenters" below 1.2× atmosphere height | 0.4% of orbital speed per day at the top of the band, up to 3× deeper (unloaded debris only) | A piece at 55 km periapsis over home reenters in about a week; 200 km debris stays for ever, as §14 wants. |
| 2026-09-23 | Debris hit rate | "Σ(debris crossing the band) × rate × relative-speed factor" | 0.006/day per overlapping object (Standard), × preset `DebrisRate`, × clamp(0.3 + Δv/300 m/s, 0.3, 2) | A depot under 60 pieces of junk takes a hit within weeks; one or two pieces are a slow-burn risk, not a coin flip. |
| 2026-09-23 | Collision threshold | "can actually hit you" | debris only: part circles overlapping while closing at ≥ 3 m/s | Controlled craft never damage each other (a 3 m/s docking bump by a 14 t tanker is a bump, and the capture speed already gates docking); a staged booster separates, so it never counts. |
| 2026-09-23 | Storm rate / warning / duration | 1 per 40 days; 1–3 d warning; 0.5–2 d | as the GDD, × preset `StormRate` × star luminosity; a storm blocks new rolls until it ends | — |
| 2026-09-23 | Fried cores | 25% chance, 3–5 days; depots/outposts 5 days | as the GDD; rolled once per craft per storm (again at each day boundary if it left shelter) | — |

| 2026-09-24 | Stuck-career grace | (not in the GDD; brief: no soft-locks) | same grace as insolvency (Standard 10 days) after cash + open advances < the cheapest launch with nothing earning | Ends a dead career instead of leaving it idling; advances and daily new offers can still rescue it inside the window. |

| 2026-09-23 | Launch cost of surface storage | parts + propellant in the tanks | surface storage tanks add no propellant to a launch quote | They launch empty (`Craft.FromDesign` fills propellant tanks only); the old quote priced a Surface Storage Tank as 40 t of xenon and made the T10 outpost a $2.11M launch instead of $170k. |
| 2026-09-23 | Tutorial designs T4/T5/T10 (test designs, not GDD values) | first draft used an M-class booster for T4/T5 and a full refinery outpost for T10 | T4 relay stack and T5 flyback booster on starter parts (+ grid fins); T10 = Outpost Core + drill + panels on an S landing stage | With M-class R&D the T4 lesson needed $1.11M against the $901k a Standard career holds after T3 (career model dead end). A double-long S booster puts the relay at 206×253 km with 11% fuel left and an S booster lands back inside 5 km; the M-class kit ($960k R&D at 10×) is now the T8 depot/tanker purchase. The refinery and storage arrive with the mining phase. |
| 2026-09-23 | Career-model assumptions (`tools/CareerSim`) | — | tanker return not flown: 240 s charged, 90% of dry parts refunded; spawned orbital set-ups pay launch cost + $22k for the recovered booster they skip; hands-on ticks 1×, idle physics 4×, rails 100,000× | Documented so the trips-per-hour number can be read against them; every one is pessimistic on money except the perfect landings a scripted pilot gets for free. |

| 2026-09-23 | Deploy/Relocate orbit windows | floor uniform between the atmosphere + 20 km and 0.7–0.8 × the low-orbit band | floor = start + span × r² (r uniform), rounded to 10 km | The career model waited ~10 days per flyable home relay (a starter stack reaches ≈250 km; the floor was uniform up to 470 km). Windows now cluster low while high ones still appear; rewards are unchanged. |

## §2 depot balance targets — measured, not met (2026-09-23)

`EconomySim` (`src/Sim/Economy/EconomySim.cs`) prices the cheapest rocket from the real catalogue for a 0.6 t lander: **direct** (one launch, booster recovered) vs **depot** (fully reusable launcher lifts the stack empty; the stack buys fuel at the tanker-delivered price and flies one way) vs **mined** (orbital fuel at 35% of the delivered price). Test system: moon Vell (toLow ≈ 960 + landing ≈ 430 m/s), neighbour Oxid.

| Configuration | Moon depot / direct | Moon mined | Neighbour depot / direct | Neighbour mined | Fuel in orbit |
|---|---|---|---|---|---|
| GDD Isp, GDD prices (Sparrow $24k) — **current** | 1.09 | 0.98 | 1.51 | 1.23 | $4.6k/t |
| vacuum engines × 2 | 1.09 | 0.98 | 0.99 | 0.80 | $4.6k/t |
| Isp × 0.85 | 1.68 | 1.48 | 1.35 | 1.07 | $5.3k/t |
| Isp × 0.7 | 1.29 | 1.07 | 1.25 | 0.87 | $7.6k/t |
| tanks × 2, vacuum engines × 2 | 1.52 | 1.35 | 1.08 | 0.90 | $5.3k/t |

Why: the depot saving is the *exponential* growth of a direct rocket with (Δv_total / exhaust velocity). Here a moon landing is ≈1,400 m/s beyond low orbit against ≈3,000 m/s exhaust velocity (0.47 e-folds), so the direct rocket is only ~1.6× the depot stack while orbital fuel costs 4–6× home fuel (the same mass-ratio tax, paid by a tanker instead). The GDD's 60% / 35% targets would need a moon leg of ≳1.5 exhaust velocities — Isp ≲ 100 s or a world ~3× larger — which contradicts the decided small scale ("launches take minutes"). Lower Isp alone does not help (both sides pay more for fuel).

What the design still delivers (no decision changed): depots make money by **selling** fuel at the ×8–×70 distance multipliers (§10) and are **required** for missions that do not fit the pad limits (§5 pad tiers: home pad 40 m / 400 t), and mined fuel is 10–30% cheaper than lifted fuel. **Decision for Nolan (flagged in STATUS.md):** accept "depots pay through sales and size limits, not by making a moon landing cheaper", or authorise a scale/Isp change. The test `DepotBalanceMeasured` guards today's numbers.


## §3 arc — career model (M12 gate, measured 2026-09-23)

`tools/CareerSim` plays one career through the flight harness with real economics (`docs/career-sim.md`, Standard preset, seed 7; `docs/career-sim-relaxed.md` for Relaxed). Assumptions are listed in the table above ("Career-model assumptions"); a scripted pilot never crashes, so the play-hours are a floor.

| phase | model (play h) | §3 arc (h) | what happened |
|---|---|---|---|
| 1 Sounding rockets (T1–T3) | 0.2 | 0–3 | $800k → $901k |
| 2 Reuse (T4–T5) | 0.4 | 3–8 | starter-class relay and flyback booster; $1.09M |
| 3 First depot (T6–T9) | 1.9 | 8–14 | 4 home relay contracts fund the first M-class purchase (Condor booster, tank M, depot controller: $1.51M incl. launches); T8 depot sells 10 t; T9 lands on Vell; $741k |
| 4 Mining (T10) | stalled at 27.5 | 14–24 | 120 home supply trips over 1,416 game days net $3.6k each; T10 ($1.17M with outpost core + drill R&D) is reached only at the model's trip cap |
| 5–6 | not reached | 24–50 | — |

**Repeat supply trips per play-hour (GDD §23): 4.48**, but the number is set by the market, not by flying: the home low-orbit node (×7.9, depth 50 t, 10-day glut half-life) absorbs one 10-t sale at a profitable price about every 11 days. Per trip: sale ≈ $63k, tanker (Condor version) $117k parts + $14k fuel + $22k booster share, 90% refund → ≈ $15k gross; the depot's $1k/day upkeep while waiting for prices ≈ $11.5k → **≈ $3.6k net**.

What this means: the home-orbit depot is a tutorial, not a business, at the current proposal numbers. The design already says money is outward (×12 at Vell, ×18–38 further out) and in contracts; the model cannot fly those routes (no general rendezvous or transfer planner in the test harness), so its stall is a floor, not proof of a dead end — the career never went insolvent and cash kept rising. But the gap to the arc is large enough to decide on before the playtest.

Options (all proposal-level unless marked):
1. **Leave it and playtest** — the arc is a proposal; Nolan's own pace on Vell contracts decides.
2. **Home node depth** K 50 → 150 t (and/or glut half-life 10 → 5 days): trips wait ~4 days instead of 11, upkeep drag drops by two thirds → ≈ $11k net per trip.
3. **Infrastructure R&D at the 8× floor of the GDD's 8–15× range** for base parts (outpost core, drill, refinery, pad kit, fabricator): T10 needs $920k instead of $1.17M; the first pad $7.4M instead of $9.2M.
4. **Depot upkeep** $1k/day → $500 (Relaxed already halves upkeep).
5. *(design change, Nolan's call)* let an outpost with a Depot Controller sell from the surface without a pad, so mined water/metal pays for the first pad.

**Relaxed preset (`docs/career-sim-relaxed.md`):** T10 at 1.6 h; the full mining outpost after 55 supply trips at 12.7 h (arc: 14–24 h — close); then the pad phase stalls, because with an outpost's upkeep added a home supply trip that waits ~11 days for the price nets less than nothing. Same conclusion: phases 5–6 need the outward routes and contracts the model does not fly, or one of the knobs above.

Nothing was changed pending his answer.
