# Perigee Fuel Co. — Game Design Document

*Working title (code name `Perigee`). Status: v0.2, 2026-09-23. Written from Nolan's brainstorm answers: 36 decisions plus 16 follow-ups, all logged in §22. The design is fully decided. Anything still marked (proposal) is a v1 number to tune in playtest.*

---

## 1. Pitch & pillars

**Pitch.** You run a small uncrewed launch company in a star system nobody has seen before, and every career generates a new one. You build rockets from a fixed catalogue of parts and fly every one by hand through real 2D orbital mechanics. You land your boosters to get most of their cost back. The real business is logistics. Lifting a tonne of fuel off the home world is expensive, so you put refueling depots in orbit and fill them with tankers. Then you mine ice on moons and asteroids so the fuel gets made in space. Sell fuel where it's scarce, take contracts, and push your network out to the edge of the system. If your cash hits $0, the company is done.

**Inspiration.** Spaceflight Simulator gives us the readable 2D physics and the flat vector look. KSP gives us the career and patched conics. Real-world propellant depots, in-space resource use and reusable boosters give us the economy. **Nothing is copied:** no SFS/KSP names, parts, art or UI.

**Pillars**

1. **Hands on the stick.** Every launch, landing, docking and supply run is flown by the player. There's no automation, no maneuver planner and no autopilot, only the trajectory preview. Skill is the product.
2. **The rocket equation is the villain.** Every system is a way to beat it: depots, booster recovery, off-world mining and off-world launch pads.
3. **Fuel is money.** Fuel is worth more the farther it sits from home. A network that moves fuel cheaply *is* the business.
4. **A new system every career.** Planets, moons, belts, atmospheres and resource deposits are all generated.
5. **Every dollar counts.** $0 ends the career. Upkeep ticks every day, and a crash is a real loss because there are no reverts.

---

## 2. Core loop

```
 take a contract / spot a price ──► design a rocket (fixed parts) ──► pay for it, launch
        ▲                                                                  │
        │                                                                  ▼
 cash ◄── sell fuel & resources at market ◄── dock, transfer, deliver ◄── fly it by hand
   │            ▲                                                          │
   │            └──── mined + refined fuel from outposts ◄── land boosters (refund) ◄──┘
   ▼
 unlock parts (R&D), build depots/outposts/off-world pads, pay upkeep ──► push farther out
                                          │
                        cash hits $0 ──► career over, final score
```

- **Minute to minute:** gravity turn, staging, booster boostback and landing, reentry angle, rendezvous and docking, pumping fuel.
- **Day to day:** pick contracts, read the market ledger, decide what to unlock next, plan which depot to fill, watch cash runway against upkeep, and hide from solar storms.
- **Career:** grow from sounding rockets to a fuel network spanning the whole system, then beat your personal best score on each preset.

### The core economic idea: why depots make launches cheaper

The rocket equation makes launch mass grow exponentially with the Δv you ask of one rocket. Depots break one big Δv budget into small legs, and that pays off three ways:

1. **Smaller rockets.** A mission rocket only needs enough Δv to reach the depot. It refuels there and continues, so the expensive stack gets smaller.
2. **Reusable upper stages.** An upper stage that refuels in orbit can finish its job *and* still have fuel to come home and land for a refund. Without depots, upper stages are almost always thrown away, and they are the biggest cost of a launch.
3. **Fuel made where it's needed.** Mined fuel on a low-gravity moon costs almost nothing to lift, and it's worth far more in orbit than fuel carried up from home.

**Balance targets (proposal, verified by economy sim tests in M7):** a depot-assisted landing on the nearest landable body (a moon or asteroid) should cost ≤ 60% of the cheapest direct design. A neighbour-planet mission should cost ≤ 35%. With mined fuel, both should drop by roughly half again.

---

## 3. Career structure & score

- **New career:** name your agency and pick a difficulty preset (§15). The game generates a star system (§12). The seed is hidden from the player, but it's stored for tests and debugging.
- **One system per save.** There is no interstellar travel.
- **Endless.** No win screen. The career runs until cash hits $0 (§10).
- **Score = net worth + exploration (decided).**
  - Net worth = cash + infrastructure at 50% of build cost + stored resources at their local market price.
  - Exploration = points per body for its first flyby, orbit and landing (e.g. 10/25/50 × a body multiplier based on Δv from home). Points convert to score at $10k each.
  - Shown live in HQ. The **peak** score is recorded, and the personal best per preset is kept on the title screen.
- **Save:** one autosaving save per career with no reverts (§18).
- **Expected arc (30–50 h, proposal):**

| Phase | Hours | What you're doing |
|---|---|---|
| 1. Sounding rockets | 0–3 | Suborbital hops, first orbit, first satellite contracts |
| 2. Reuse | 3–8 | Booster landings, drone ship, cheap access to low orbit |
| 3. First depot | 8–14 | Tankers fill a low-orbit depot; first landings on another body; first fuel sales |
| 4. Mining | 14–24 | Scan nearby moons and asteroids, build ice/CO₂ outposts, refine methalox |
| 5. Off-world pads | 24–32 | Build rockets on a moon, and launch them from shallow gravity wells |
| 6. The network | 32–50+ | Depots at gas-giant moons and outer ice, xenon scooping, deep contracts |

---

## 4. Flight & physics

### Scale (proposal)

The system is scaled down like SFS, so launches take minutes, not hours.

| Thing | Value |
|---|---|
| Home radius / surface gravity | 450 km / 9.81 m/s² (GM ≈ 1.99×10¹² m³/s²) |
| Home atmosphere | 50 km high, scale height 6 km |
| Home rotation = **1 day** | 6 h game time (surface speed ≈ 130 m/s, so east launches get a boost) |
| Low home orbit | 60–120 km; ≈ 1,950 m/s; period ≈ 28 min |
| Δv to low home orbit | ≈ 2,600 m/s including losses |
| Home moon (random, §12) | when one exists, a typical one is radius 150–250 km, orbit 10–15 Mm, Δv ≈ 1,600 m/s from low home orbit to its surface |
| Home year | ≈ 400 days; neighbour-planet transfers take ≈ 100–250 days |
| Home sphere of influence | ≈ 60 Mm |

### Orbits: patched conics (decided)

- One body's gravity acts at a time: whichever sphere of influence (SOI) the craft is in.
- Everything is coplanar because the game is 2D, so there's no inclination.
- **On rails:** any craft coasting in vacuum follows its conic analytically (a Kepler solver covering ellipse, parabola and hyperbola). This is exact, so time warp can't drift or skip events.
- **Active physics:** the craft you control, while thrusting, in an atmosphere, landed or near another craft, is integrated at a fixed 50 Hz step in double precision (proposal). When it stops thrusting in vacuum it goes back on rails.
- **SOI changes** are computed analytically (bracket plus bisection on distance), so warp stops exactly at the transition.

### Time warp (proposal)

- On-rails warp levels: 1×, 5×, 10×, 50×, 100×, 1k×, 10k×, 100k×. There are no altitude limits, because coasting is exact.
- Physics warp of 1–4× is allowed while thrusting or in an atmosphere.
- Warp auto-stops on: SOI change, atmosphere entry, a contract deadline within 1 day, a solar-storm warning, low cash runway, or a loaded craft coming within range.

### Craft physics

- Each craft is **one rigid body** made of parts, with mass, center of mass and moment of inertia recomputed as fuel drains and stages drop. There is no wobble.
- **Breakup:** every part has an impact tolerance in m/s. A hit above it destroys that part, and the craft splits into new rigid bodies wherever the attachment tree breaks.
- **Terrain:** each body's surface is a radial heightmap (noise) around its circumference. Home has oceans at sea level.
- **Landed** craft are fixed to the surface in the body's rotating frame. Landing legs have higher tolerance and a simple spring.

### Atmosphere: drag + reentry heat (decided)

- **Density:** ρ(h) = ρ₀·e^(−h/H) per body.
- **Drag** per part = ½ρv²·Cd·A, where A is the part's exposed width across the airflow. Parts shadowed by parts in front of them (a 2D ray test along the velocity vector) get no drag and no heat, so nose cones, fairings and heat shields matter.
- **Stability:** center of pressure vs center of mass. Fins and grid fins add lift and torque (flat-plate model). A simple transonic drag bump (proposal).
- **Heat:** exposed leading parts take heat flux ∝ √ρ·v³ (Sutton–Graves form). Parts store heat and radiate it away, and a part is destroyed above its max temperature. Heat shields have high tolerance plus an ablator that runs out. Reentry glow and plasma trail are visual cues.
- **Parachutes:** staged or keyed. Each has a max safe deploy speed. They work anywhere there's air.
- **Aerobraking** at planets with atmospheres is a major fuel saver, and a major way to burn up.

### Trajectory preview only (decided)

There are no maneuver nodes, no burn planner and no attitude hold. You fly by watching the predicted path change as you burn.

- The predicted conic, drawn in both flight and map view, with up to **3 patches** ahead (current orbit, next SOI encounter, and the one after).
- Apoapsis/periapsis markers with altitude and time-to.
- Atmosphere-entry and surface-impact markers.
- **Target markers (decided):** right-click any craft or body as a target to see closest approach on your predicted path: distance, time and relative speed. This is still preview, not planning.
- **Readouts (decided; also in the builder):** remaining Δv and burn time per stage, surface and orbital speed, vertical speed, and heat warnings.

### Controls (keyboard + mouse, decided)

| Input | Action |
|---|---|
| A / D | Rotate. **(decided)** Releasing the keys damps rotation to a stop, like SFS. It doesn't hold a heading. |
| Shift / Ctrl, Z / X | Throttle up / down, full / cut |
| Space | Next stage |
| R, then I/K/J/L | RCS on/off; translate fore/aft/left/right (for docking) |
| G | Landing legs |
| M or mouse wheel | Map view. **(decided)** One seamless zoom from a single part out to the whole system. |
| , / . / / | Warp down / up / back to 1× |
| Tab | Cycle nearby craft; click any craft in the map to switch to it |
| Right-click | Set target |
| Esc | Pause |

### Multiple craft

- One craft is active. All others are on rails (or landed and static).
- **Loaded bubble (proposal):** craft within 2.5 km of the active one are fully simulated, so docking and collisions are real.
- You can switch craft at any time, and switching doesn't pass time.

### Booster flashback (decided)

Pilot-landed recovery is decided, but in real life the upper stage and the booster fly at the same time, and one player can't fly both. Proposed fix:

1. At staging, you keep flying the upper stage. The separated booster is **held**, frozen at its separation state.
2. Once the upper stage is coasting on rails (orbit, or a suborbital arc out of the air), a **"Fly booster"** prompt appears.
3. The booster resumes from its separation moment and you fly it home. It interacts with nothing else, so replaying its flight is consistent. When it lands or is lost, the clock returns to the upper stage's time.
4. If you skip the prompt, the booster is lost. Multiple boosters (sides plus core) queue in staging order, each one optional.

This isn't a revert: the upper stage's outcome is already locked in. It only unfreezes a craft nobody was flying.

---

## 5. Rockets & parts

### Builder (decided: fixed parts only)

- Parts snap to **stack nodes** (top/bottom) and to **radial** surface points. There is **2D mirror symmetry**.
- The **staging list** is auto-built and editable.
- **Live stats per stage (proposal):** mass, Δv at sea level and in vacuum, TWR at home surface, burn time, total cost, and the **refund if recovered**.
- Designs are saved to a design library. You can launch from any pad you own.
- **Pad tiers (proposal):** each pad has a height and mass limit, and bigger pads are infrastructure you buy (§10).

### Part catalogue (proposal, about 60 parts)

There are three diameters: **S** (1.0 m), **M** (2.0 m) and **L** (3.5 m). Names are placeholders.

| Category | Parts |
|---|---|
| Probe cores | Core S (small reaction wheel and battery), Core M (strong wheel), **Hardened Core** (storm-proof, §14), **Depot Controller** (turns a craft into a depot, §7) |
| Methalox tanks | S short/long · M short/medium/long · L medium/long/jumbo |
| Other tanks | RCS propellant (inline S, radial), Xenon (radial, inline S) |
| Cargo | Resource bin S/M (ice, ore, CO₂), fairings S/M/L |
| Engines | **Kestrel** S sea-level (Isp 290/320 s) · **Sparrow** S vacuum (345 s) · **Condor** M sea-level, deep throttle and restartable (reusable booster engine, 300/330 s) · **Heron** M vacuum (355 s) · **Titan** L sea-level heavy (295/325 s) · **Lander** S deep-throttle (320 s) · **Ion drive** (xenon + power, 4,000 s, tiny thrust) |
| RCS | Thruster block (2-way), linear thruster. HTP propellant, 160 s. |
| Structure | Decouplers S/M/L, radial decoupler, adapters S–M and M–L, nose cones S/M/L, truss |
| Docking | Docking port (standard), **Heavy port** (larger, faster pumping), **Claw** (grabs anything, including debris; can't pump) |
| Aero & recovery | Fin, grid fin (deployable, for booster return), heat shields S/M/L, parachutes (nose, radial), drogue, landing legs S/M/L |
| Power | Solar panel (folding; output falls with distance from the star as 1/d²), battery, RTG (expensive; works in shadow and the outer system) |
| Survey & mining | Resource scanner, drill, refinery S/L, atmospheric **scoop** (xenon from gas giants) |
| Base | **Outpost Core**, surface storage tank, **Pad Kit** (off-world launch pad), **Fabricator** (builds parts from metal) |

Every part has: mass, unit cost, R&D price, drag profile, max temperature, impact tolerance and polygon art.

---

## 6. Recovery & reuse (decided: land it yourself for a refund)

- **Refunds (proposal):**

| Where it lands | Refund of intact parts |
|---|---|
| Your home pad or landing zone | 90% |
| Your drone ship (bought, sits downrange at sea) | 85% |
| Any other dry land on home | 50% (transport) |
| Ocean, except the drone ship | 0% |
| One of your off-world pads | 90%, of which the metal share goes into that outpost's storage and the rest is paid in cash (§9) |

- Leftover fuel in recovered tanks is refunded at the local price.
- Refunds are paid in cash when the craft is recovered. There's no used-parts inventory to manage.
- Upper stages and payloads can be recovered too: reenter, then chute or propulsive-land.
- Any spent stage left in orbit without a probe core becomes **debris** (§14).

---

## 7. Docking, fuel transfer & depots

- **Docking (decided: manual dock, then transfer).** Ports capture within 0.5 m, 5° alignment and < 0.5 m/s closing speed (proposal). Soft capture turns into a hard dock after a second.
- **Transfer:** pick source and destination tanks in a pump panel, then pump for game time. Rates (proposal): standard port 0.5 t/s, heavy port 2 t/s. Warp is allowed while docked, since docked craft ride the rails as one.
- **Claw:** latches onto anything, for debris, derelicts and relocation contracts. It can't pump fuel.
- **Depots:** any craft with a **Depot Controller** part is a depot.
  - It can **sell** from its tanks to the local market node (§10).
  - It appears in the **Logistics** panel with stock and upkeep.
  - It **pays upkeep** (decided: small upkeep on infrastructure).
  - **Boil-off (decided):** methalox in tanks *without* a Depot Controller boils off at 1%/day. The controller's cryocooler stops boil-off, which is what the upkeep pays for.
- Plain craft can still hold and hand over fuel with no upkeep. They just can't sell, and they slowly lose fuel.

---

## 8. Fuels & resources

### Fuels (decided: methalox + a few specials)

| Fuel | Used by | Where it comes from |
|---|---|---|
| **Methalox** (one combined resource) | Every chemical engine | Bought at home, or refined from water + CO₂ |
| **RCS propellant** (high-test peroxide) | RCS thrusters | Bought at home, or refined from water |
| **Xenon** | Ion drives | Bought at home (expensive), or scooped from gas-giant atmospheres |
| Electricity | Ion drives, refineries, cores, drills | Solar, RTG, batteries. It isn't traded. |

### Raw → product chains (proposal)

| Input | Machine | Output |
|---|---|---|
| Water ice | Refinery | Water |
| Water + CO₂ | Refinery (power-hungry) | Methalox. Mass balances like the real chemistry, CO₂ + 2 H₂O → CH₄ + 2 O₂: 1 t methalox needs ≈ 0.55 t CO₂ + 0.45 t water. |
| Water | Refinery | RCS propellant |
| Ore | Refinery | Metal |
| Gas-giant atmosphere | Scoop (flown through the upper atmosphere) | Xenon (small amounts per pass) |

- **CO₂ sources:** dry ice, carbonate rock, CO₂ atmospheres (via scoop or a surface intake) and comets. A body with only ice can make water and RCS propellant but **not methalox**, which is what makes bodies with ice *and* CO₂ valuable.
- Only products sell (water, methalox, RCS propellant, xenon, metal). Raw ice, ore and CO₂ have to be refined first.

---

## 9. Mining, outposts & off-world pads (decided: outposts + off-world pads)

- **Scanning (decided):** every body's orbit is known from day one, but its resource deposits aren't. One full orbit with a **resource scanner** maps the deposits around the body's circumference. In 2D a single orbit covers the whole surface. Landing a drill reveals a deposit's exact richness.
- **Outpost Core:** land a craft carrying one on flat ground and it becomes a permanent base. More modules join by landing within 200 m of the core (proposal).
- **Drills** extract from the deposit underneath them. Rate by richness, 0.5–4 t/day (proposal).
- **Refineries** convert raw to product using power. Solar output falls with distance from the star, and night on a rotating body is averaged. RTGs fix both problems at a price.
- **Background production:** outposts produce whenever time passes, including warp and while you fly elsewhere, capped by storage. **Moving** the product is always flown by hand (decided: no automation).
- **Where you can sell (decided):** in orbit, plus on the surface of any body where you own an off-world pad. An outpost without a pad has to fly its output up to an orbital market or a depot. A pad outpost can sell straight from its storage, which is part of what the pad's upkeep buys.
- **Off-world pads:** a **Pad Kit** + **Fabricator** next to an Outpost Core. Rockets are built and launched there using local fuel, which is a huge advantage in low gravity.
  - **Part cost off-world (decided; ratios proposal):** each part's price splits into a **metal share** (1.5 × part mass in metal from local storage) and a **cash share** (50% of the part's unit price, for "electronics and precision parts").

---

## 10. Economy

### Money

- **Income (decided):** contracts + selling fuel and resources. There's no science tree and no milestone payouts.
- **Costs:**
  - Part R&D, a one-time unlock (decided).
  - Per-unit part cost at every launch.
  - Fuel bought at home.
  - Infrastructure build cost and daily upkeep.
- **R&D (decided: buy parts with money):** every part is purchasable from day one. Price is the only gate. The R&D price is about 8–15× the part's unit cost (proposal). A starter set is pre-unlocked: Core S, S tanks, Kestrel, Sparrow, decoupler S, nose cone S, fin, parachute and legs S.

### Anchor prices (proposal, Normal preset)

| Item | Price |
|---|---|
| Methalox at home | $1.0k / t |
| RCS propellant at home | $3k / t |
| Xenon at home | $40k / t |
| Core S / Kestrel / Condor / Titan | $6k / $12k / $55k / $180k |
| Depot Controller / Outpost Core / Pad Kit / Fabricator | $30k / $60k / $400k / $300k |
| Starting cash | $800k |

### Upkeep per day (decided: small, on infrastructure only; numbers proposal)

| Infrastructure | Upkeep |
|---|---|
| Starting home pad | free |
| Extra/bigger home pad | $4k |
| Drone ship | $2k |
| Depot (per Depot Controller) | $1k |
| Outpost (per Outpost Core) | $3k |
| Off-world pad | $8k |
| Probes and ordinary craft | free |

The HUD always shows **cash runway** = cash ÷ daily upkeep, in days.

### Market (decided: supply & demand)

- **Nodes (proposal):**
  - **Home surface:** buy and sell at fixed prices with unlimited depth. It's the **only place you can buy**.
  - **Low orbit of every body:** sell only. A node covers stable orbits between the atmosphere (or surface) and 1.5× the body's radius.
  - **Surface of any body with your off-world pad (decided):** sell only, from that outpost's storage. It has its own glut and its Δv multiplier is measured to that surface.
- **Distance multiplier (decided; formula proposal):** price = base × M, where M = 1 + 5·(e^(Δv/3000) − 1) and Δv is measured from home surface to that node. Examples: low home orbit ≈ ×8, low orbit of a typical home moon ≈ ×13, outer planets ≈ ×60–70.
- **Saturation:** every tonne you sell adds to that node's glut G for that resource. The price is base × M ÷ (1 + G/K). K is the node's depth (e.g. 50 t for methalox in low home orbit). G decays with a **10-day half-life**, so prices recover.
- The sell panel shows the **price impact before you confirm**.
- The ledger in HQ shows every node's current prices and a 30-day trend.

### Bankruptcy (decided: instant game over at $0, no loans)

- Purchases and launches that would take cash below $0 are blocked.
- **Career over** the moment cash is ≤ $0 **and** nothing in flight can still earn money.
- **Grace (decided):** this implements the "no assets in flight" clause of the answer. An *earning craft* is a non-infrastructure craft carrying sellable cargo or an accepted contract payload. While one exists, upkeep can push cash below $0 for **up to 10 days**. A red "INSOLVENT — day 3/10" banner shows, and the career ends if cash is still ≤ $0 when the grace runs out or the last earning craft is lost.
- The end screen shows the logbook: days survived, bodies reached, peak score, and the cause ("upkeep", "lost tanker", …).

---

## 11. Contracts & tutorial

### Board (proposal)

- 3–6 open offers, refreshed daily. Up to 5 accepted at a time.
- Each contract has a procedural client, objective, deadline, reward, optional 20% advance, and a failure penalty (repay the advance + 10% of the reward).
- Offers only target bodies you've already reached, plus the next one out, so contracts pull you outward.

### Contract types (proposal)

| Type | Objective |
|---|---|
| Deploy | Put a client payload (provided free) into an orbit window around a body |
| Fuel delivery | Bring X t of a resource to a client station and dock to hand it over |
| Refuel | Dock with a client satellite and top it up |
| Survey | Fly by, orbit, scan or land on a body |
| Retrieve | Bring a client payload or derelict down to home surface intact |
| Debris cleanup | Deorbit N debris objects in an orbit band (claw + burn) |
| Relocate | Move a client satellite to a new orbit |
| Supply | Deliver water or metal to a client outpost or station |

Client stations and satellites are real NPC craft on rails that you have to rendezvous and dock with.

### Tutorial = early contracts (decided)

These are fixed first offers that pay real money, with on-screen hints the first time each skill comes up. Rewards are proposals.

| # | Contract | Teaches | Reward |
|---|---|---|---|
| T1 | Sounding Rocket: reach 20 km and land safely | Builder, staging, throttle, chutes | $40k |
| T2 | Space Is Up: cross 50 km | Gravity turn, drag | $60k |
| T3 | First Orbit | Trajectory preview, apo/peri | $120k |
| T4 | Comsat: 200–300 km circular | Precise orbits, fairings | $180k |
| T5 | Bring It Back: land a booster on the pad | Booster flashback, grid fins | $150k |
| T6 | Handshake: dock with a client test target | Rendezvous, target markers, RCS | $220k |
| T7 | Top Up: pump 2 t into a client satellite | Pump panel | $200k |
| T8 | Gas Station: build a depot and sell 10 t | Depot Controller, market | $300k |
| T9 | Touchdown: land a probe on the nearest landable body (moon or asteroid) | Transfers, landing | $400k |
| T10 | Dig: outpost + drill on the nearest ice deposit | Scanning, outposts | $600k |

---

## 12. Procedural star system (decided: one per save, 6–10 planets, home always Earth-like)

- **Star:** G or K type (proposal), so home can sit in the habitable zone. Luminosity sets solar-panel output at each distance. The system gets its own storm-rate factor (§14).
- **Home:** fixed physical stats (§4), with continents and oceans and a flat launch site on land. **Moons are random (decided):** home can have none, a barren one, or a rich one. To make sure the early game always has a first target, the generator guarantees **water ice and ore on some body within ≈ 2,500 m/s of low home orbit** (proposal). That body can be a moon, a near-home asteroid or a neighbour planet's moon.
- **Planet mix (proposal):**
  - 0–3 inner rocky (airless or thick hot atmosphere)
  - Home
  - 0–2 Mars-like rocky with thin CO₂ atmospheres (a CO₂ source)
  - An **asteroid belt**: 15–40 named, landable asteroids plus a visual band
  - 1–3 **gas giants**, each with 2–6 moons (xenon scooping)
  - 0–2 ice giants
  - Outer icy dwarfs
  - 1–3 **comets** on eccentric orbits (ice + CO₂)
  - 8–25 moons total
- **Per body:** radius, density class, atmosphere (none/thin/thick: height, density, composition), terrain noise, palette, rotation, coplanar orbital elements and resource deposits (type, richness, arc of the circumference).
- **Validation, tested across 1,000 seeds (proposal):**
  - No overlapping SOIs, and planets spaced by a minimum number of mutual Hill radii.
  - No moon inside the Roche limit.
  - Every body reachable within set Δv budgets.
  - Ice and ore within ≈ 2,500 m/s of low home orbit (see Home above).
  - At least one CO₂ source within ~5 km/s of low home orbit.
  - A launch site exists.
- **Names:** pronounceable names from syllable tables. The player names the agency.
- **Exploration:** orbits are known from the start. Deposits need scanning (§9). Flybys, orbits and landings score points (§3).

---

## 13. Rendering the system in 2D

- Everything is in one plane. Bodies are filled circles with a terrain polygon and an atmosphere gradient ring.
- The map shows every conic as a thin line colored per body, SOI circles on hover, your predicted path, target markers, a **debris density overlay** per orbit band, and **shadow cones** behind planets during storms (§14).
- **Seamless zoom** from part scale to system scale (decided). The flight view becomes the map with no screen cut.

---

## 14. Hazards (decided: orbital debris + solar storms; no part failures)

### Orbital debris

- Any detached stage or part left in orbit without a probe core becomes **debris**, a tracked object on rails.
- **Decay (proposal):** debris with its periapsis below 1.2× the atmosphere height slowly sinks and reenters. Low orbits clean themselves; high orbits don't.
- **Risk off-screen (proposal):** each day, each infrastructure craft gets a hit chance of Σ(debris whose orbit crosses its altitude band) × rate × relative-speed factor. A hit destroys one random part, which can split the craft and **spawn more debris**. That's a cascade, and it's capped.
- **Near the active craft:** debris is real physics and can actually hit you.
- **Cleanup:** grab it with a claw and deorbit it, or take debris-cleanup contracts.
- Performance cap: 2,000 tracked objects (proposal).

### Solar storms

- **Rate (proposal):** Normal averages one every 40 days, adjusted by preset and by the star.
- **Warning:** 1–3 days ahead. Warp stops, a HUD alert shows, and the map draws shadow cones.
- **Duration:** 0.5–2 days.
- **Who's at risk:** any craft in open space. Safe places (decided) are **inside a body's shadow cone** (in 2D this is simple geometry, so park behind a planet or moon), landed, or within 3× home's radius (its magnetosphere).
- **Damage (proposal):**
  - Standard cores: 25% chance per storm to be **fried** (decided: offline, then recovers). The craft goes dark and uncontrollable for 3–5 days, riding the rails (or sitting landed), then reboots on its own. A fried craft on a decaying or impact path can be lost.
  - **Hardened Core:** immune.
  - Depot Controllers and Outpost Cores go **offline** for 5 days: no selling, no cooling, no production. Hardened variants cost 3×.
- Storms turn the warning window into a flying job: move tankers into shadow, or accept the risk.

---

## 15. Difficulty presets (decided: presets; values proposal)

| Setting | Relaxed | Standard | Brutal |
|---|---|---|---|
| Starting cash | $1.5M | $800k | $400k |
| Part prices | ×0.75 | ×1 | ×1.3 |
| Upkeep | ×0.5 | ×1 | ×1.5 |
| Contract pay | ×1.25 | ×1 | ×0.85 |
| Storm rate | ×0.5 | ×1 | ×1.5 |
| Debris hit rate | ×0.5 | ×1 | ×2 |
| Market depth K | ×1.5 | ×1 | ×0.7 |
| Insolvency grace | 20 days | 10 days | 3 days |

---

## 16. UI & screens

- **Title:** New Career · Continue · Settings · Quit, with personal bests per preset.
- **HQ hub:**
  - **Contracts:** the board.
  - **Market:** the ledger for every node.
  - **R&D:** the parts shop.
  - **Finances:** cash, runway, upkeep breakdown, 60-day history graph.
  - **Logistics:** every depot, outpost and pad, with stock, production, upkeep and status.
  - **Score:** live.
- **Builder:** part catalogue by category, stage list, live stats, design library, launch-site picker.
- **Flight HUD:**
  - Altitude, surface and orbital speed, vertical speed, throttle.
  - Per-stage fuel and Δv, heat warnings.
  - Apo/peri, target closest approach.
  - Game date, warp, cash, runway, storm alerts.
- **Map:** as §13, plus market prices on hover over any node.

---

## 17. Art direction (decided: clean flat vector, drawn in code/SVG)

- **Parts are polygon data** (JSON: polygons + fill colors + outline), drawn with Godot's `Polygon2D`/`_Draw`, so they're crisp at every zoom. No raster sprites for anything that scales.
- **Planets** are generated from their parameters: base circle, terrain polygon, palette per type, atmosphere gradient shader. A gas giant gets flat bands. A comet gets a flat-shape tail near the star.
- **Palette:** a dark navy space background, off-white orbit lines and one saturated accent per body type. Parts are mostly white and gray with an orange/black accent. Original, not SFS colors.
- **FX:** engine plumes (shader gradient, widening as air pressure drops), reentry glow, flat-shape explosions and debris, sparks on docking capture.
- **Renderer (proposal):** Mobile (Vulkan) with 2D MSAA ×4. GL Compatibility doesn't support `msaa_2d`, and flat vector edges look jagged without it.
- **Font:** an open-license (OFL) sans for all UI.

---

## 18. Audio & saves

**Audio (decided: ambient synth score).**
- Adaptive music: a swell at launch, a calm pad in orbit, a low pulse during docking, a warning motif before storms, silence on the insolvency screen.
- **Engine sound scales with air density.** In vacuum it's a muffled rumble carried through the structure.
- SFX: staging bangs, docking clamps, pump flow, radio beeps, reentry crackle, parachute snap.
- **Source (proposal):** CC0/royalty-free placeholder tracks during development, with original tracks before release.

**Saves (decided: one autosave per career, no reverts).**
- Autosave triggers (proposal): warp stops, docking, landing, recovery, purchases, every 3 real minutes, and quit.
- Save format: JSON of the full sim state, gzip-compressed.
- Booster flashback (§4) isn't a revert.

---

## 19. Steam (decided: achievements only)

No cloud saves, Workshop or leaderboards. Achievements (proposal, 20):

| Achievement | How |
|---|---|
| Liftoff | First launch |
| Up and Out | Cross the atmosphere line |
| Around Again | First orbit |
| Stuck the Landing | First booster recovery |
| Hat Trick | Recover the same design's booster 3 flights in a row |
| Handshake | First docking |
| Gas Station | First depot sale |
| First Touchdown | Land on another body |
| Dig In | First outpost |
| Home Brew | Refine your first tonne of methalox off-world |
| Off-World Liftoff | Launch from an off-world pad |
| Skimmer | Scoop xenon from a gas giant |
| Belt Buckle | Land on 5 asteroids |
| Cleaner | Deorbit 25 debris objects |
| Weathered It | Survive a storm with every craft safe |
| Grand Tour | Orbit every planet in one career |
| Millionaire / Billionaire | Net worth $10M / $1B |
| Shoestring | Reach low orbit with under $50k cash |
| Brutal Survivor | 500 days on Brutal |

---

## 20. Technical architecture

- **Godot 4.7.2 .NET, C# on net8.0.** Installed user-local; `~/.claude/workspace/tools/languages.md` has the working project shape. PC/Steam, single-player, keyboard + mouse.
- **Layout (proposal):**
  - `Perigee.csproj`: `<Project Sdk="Godot.NET.Sdk/4.7.2">`, with `<Compile Remove="src/**;tests/**" />`.
  - `src/Sim/Sim.csproj`: a pure C# library with no Godot dependency.
    - `Orbits`: 2D Kepler solver, state↔elements, conic propagation, SOI transitions.
    - `Craft`: part tree, mass properties, forces, staging, breakup.
    - `Aero`, `Heat`, `Terrain`, `Docking`
    - `Economy`, `Market`, `Contracts`, `Mining`
    - `Generator`: star system and validation.
    - `Debris`, `Storms`, `Save`
  - `game/`: Godot scenes and nodes for rendering, input, UI and audio. They read the sim and send it commands.
  - `tests/Sim.Tests`: xUnit, run headless with `dotnet test`.
  - `.gdignore` in `src/`, `tests/` and `docs/`.
- **Precision:** the sim uses its own `Vec2d` (double). Godot 2D is float32, so rendering is **relative to a floating origin** at the camera. The world is drawn around it, never at raw system coordinates.
- **Physics:** custom, not Godot Physics. Godot's is float32 and knows nothing about orbits.
  - Fixed 50 Hz step for active craft.
  - Separating-axis collision between part polygons and terrain segments, and between craft in the loaded bubble.
- **Determinism:** seeded RNG per career (generator, contracts, storms, debris rolls), so saves and tests reproduce.
- **Save:** System.Text.Json + gzip.
- **Steam:** Steamworks.NET for achievements, confirmed at the Steam milestone.
- **Performance target:** 60 fps with a 250-part craft in atmosphere, 2,000 on-rails objects, and every conic drawn in the map.
- **Tests that gate the design (proposal):**
  - Kepler state↔elements round trip to 1e-9 relative.
  - Energy and angular momentum exactly conserved on rails.
  - SOI transition times vs brute-force integration.
  - Builder Δv readout vs a simulated burn within 1%.
  - Generator validation across 1,000 seeds.
  - Economy sim: the depot balance targets from §2 hold.
  - Market saturation and recovery curves.
  - Bankruptcy and grace rules.

---

## 21. Roadmap: build everything, in order

Nolan chose to build the whole design before the first playtest (decision #28). This is the order of work. Every gate is automated (tests, scripted flights, sims, screenshots), so no milestone waits on a human. Nolan's first playtest comes after M13.

| # | Milestone | Done when |
|---|---|---|
| 0 | Skeleton: csproj, Sim library, tests, headless build, scripted screenshots | `dotnet build` + `dotnet test` green, window opens |
| 1 | **Orbit core:** Kepler solver, conics, SOI patches, exact warp, map view, seamless zoom | Orbit tests green; a test body system renders and warps cleanly |
| 2 | **Flight feel:** craft rigid body, engines, throttle, rotation damping, staging, terrain, landing/crash, drag, heat, chutes, trajectory preview | Scripted launch → orbit → deorbit → landing passes; flight-feel self-review with screenshots |
| 3 | Builder: catalogue, snapping, symmetry, staging editor, live stats, design library | Δv readout matches simulated burns |
| 4 | Multi-craft, loaded bubble, RCS, targets, docking, pump panel, claw | Scripted rendezvous, dock and transfer passes |
| 5 | Recovery: booster flashback, grid fins, drone ship, refunds | Scripted booster-flashback pad landing passes |
| 6 | Procedural system: generator, validation, deposits, scanning, names | 1,000-seed validation green |
| 7 | Economy: cash, R&D shop, launch costs, upkeep, market nodes, saturation, bankruptcy and grace | Economy sim: depot balance targets hold |
| 8 | Contracts: board, 8 types, NPC client craft, tutorial chain T1–T10 | Scripted run of T1–T10 passes |
| 9 | Mining: outposts, drills, refineries, power, background production, scoop | Off-world methalox chain sim test |
| 10 | Off-world pads: Pad Kit, Fabricator, metal split, local launches | Scripted off-world build + launch passes |
| 11 | Hazards: debris, decay, cascades, overlay; storms, shadow cones, hardened parts | Scripted storm and debris tests |
| 12 | Career wrapper: title, new career, presets, score, personal bests, autosave, HQ screens, end logbook | Simulated 50-hour career matches the §3 arc, with no exploits and no dead ends |
| 13 | Art pass, audio pass, Steam achievements, balance | Release candidate; then Nolan's first playtest |

---

## 22. Decisions log (brainstorm, 2026-09-23)

| # | Question | Answer |
|---|---|---|
| 1 | Main mode | Career only (no sandbox) |
| 2 | Orbital physics | Patched conics |
| 3 | Solar system | Procedural systems |
| 4 | Fuel source for depots | Tankers early, off-world mining later |
| 5 | Building | Fixed parts only |
| 6 | Stage reuse | Pilot-landed recovery for a refund |
| 7 | Supply automation | Always manual |
| 8 | Engine / platform | Godot C#, PC/Steam |
| 9 | Interstellar | One system per save |
| 10 | Crew | Probes only |
| 11 | Atmosphere | Drag + reentry heat |
| 12 | Income | Contracts + selling fuel and resources |
| 13 | Part unlocks | Buy with money (R&D), then per-unit cost |
| 14 | Failure | Instant game over at $0 (no loans) |
| 15 | Art style | Clean flat vector |
| 16 | Flight aids | Trajectory preview only |
| 17 | Market | Supply & demand |
| 18 | Fuel types | Methalox + RCS propellant + xenon |
| 19 | Bases | Mining outposts + off-world pads |
| 20 | Fuel transfer | Dock, then transfer |
| 21 | Recurring costs | Small upkeep on infrastructure |
| 22 | Home world | Always Earth-like, rest generated |
| 23 | Hazards | Orbital debris + solar storms |
| 24 | Controls | Keyboard + mouse |
| 25 | Win condition | Endless, with a score |
| 26 | System size | 6–10 planets + moons + belts |
| 27 | Career length | 30–50 hours |
| 28 | First build | Everything |
| 29 | Working title | Perigee → **Perigee Fuel Co.** (see #52) |
| 30 | Audio | Ambient synth score |
| 31 | Difficulty | Presets |
| 32 | Saves | One autosave per career, no reverts |
| 33 | Players | Single-player |
| 34 | Tutorial | Early contracts teach |
| 35 | Art pipeline | Drawn in code / SVG |
| 36 | Steam | Achievements only |

**Follow-up round (§23 proposals, 2026-09-23)**

| # | Question | Answer |
|---|---|---|
| 37 | Booster flashback | Yes |
| 38 | Rotation damping on key release | Yes (no heading hold) |
| 39 | Target closest-approach markers | Yes |
| 40 | Δv and burn-time readouts | In flight + builder |
| 41 | Map | Seamless zoom |
| 42 | Where you can sell | Orbit + surfaces where you own an off-world pad |
| 43 | Price curve by Δv from home | As drafted (≈ ×8 / ×13 / ×60–70) |
| 44 | Insolvency grace | 10 days while an earning craft is in flight |
| 45 | Boil-off without a Depot Controller | Yes, 1%/day |
| 46 | Resource deposits | Hidden until scanned |
| 47 | Home moon | Random (the generator still guarantees ice + ore nearby) |
| 48 | Off-world part cost | Metal + 50% cash |
| 49 | Fried probe core | Offline for a few days, then recovers |
| 50 | Storm shelter | Shadow cones |
| 51 | Score | Net worth + exploration |
| 52 | Title | **Perigee Fuel Co.** ("Perigee" alone is a 2018 Steam puzzle game, app 661070) |

---

## 23. Open items

- **No open design questions.** All 16 proposals were answered (§22 #37–52). Every remaining (proposal) tag is a v1 number to tune in playtest.
- **Before any store page:** a trademark search on "Perigee Fuel Co." (the Steam title search is done).

### Design risk to watch

Every supply run is flown by hand with preview-only aids, across a 30–50 hour career. Repeat depot runs could turn into a grind. The design pushes against that already:
- Bigger tankers mean fewer trips.
- Off-world pads shorten routes.
- Prices rise outward, so new routes pay more than old ones.
- Contracts keep adding variety.

**Measure it** in the M12 career sim and in Nolan's playtest: count repeat supply trips per hour of play, and bring the numbers to Nolan before changing any decision.
