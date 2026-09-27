# First playtest — Perigee Fuel Co. 0.9.0-rc1

Everything below runs from `build/linux/PerigeeFuelCo.x86_64` (or `godot --path .` in the repo). Saves live in `~/.local/share/godot/app_userdata/Perigee Fuel Co/`; delete that folder for a clean slate. Nothing is sent anywhere; there is no Steam in this build (achievements are recorded locally).

## The three things I most want you to test

1. **Booster flashback (T5).** Launch the tutorial's fifth contract, stage the booster, press Enter when "Fly booster" appears, burn back, land on the engine within 5 km of the pad. Does the rewind feel like a second chance or like a cheat? Is the 90% refund worth the minute it costs?
2. **Docking (T6–T8).** RCS on `R`, translate with `IJKL`, close the last metres with the port markers. Ports capture within 0.5 m, 5° and 0.5 m/s. Tell me where you got lost: target markers, the closing-speed readout, or the pump panel (`F`) afterwards.
3. **The money after T9.** The career model (docs/career-sim.md) says a home-orbit depot barely pays after the tutorial, and that the mining outpost (T10) is a long grind unless you take Vell contracts or sell at Vell. Play from T8 to T10 the way *you* would and tell me whether the pacing felt like GDD §3 (first depot 8–14 h, mining 14–24 h) or like a wall. BALANCE.md lists five knobs; I have not turned any.

## Suggested order (about 3 hours)

| # | Do | Watch for |
|---|---|---|
| 1 | Title → New Career, Standard | name entry, preset text, the generated system on the map (`M`) |
| 2 | T1 Sounding Rocket | builder (`B`): staging order, Δv/TWR readouts, tooltips on every part; launch, chute (`P`), landing |
| 3 | T2/T3 First Orbit | gravity turn feel, the apo/peri readouts, warp (`,` `.`), the map at each zoom band |
| 4 | T4 Comsat, T5 flashback | the 200–300 km window on the board; the "Fly booster" prompt; the refund line on recovery |
| 5 | T6/T7 docking + pumping | RCS authority, capture cue, pump panel |
| 6 | T8 depot + sale | HQ (`H`) Market tab: the price you see vs the price you got; the ledger trend after selling |
| 7 | T9 Vell landing | transfer timing without an autopilot, capture burn, the descent readouts (AGL, vertical speed) |
| 8 | Save, quit, Continue | the autosave comes back exactly; the HQ Finances graph makes sense |
| 9 | Settings | volumes per bus, fullscreen, UI scale, rebinding one key and resetting |
| 10 | Let a storm come (warp a few days) | the warning banner, the shadow cone on the map, a fried core rebooting |

## What I need back

- Anything that was **not readable** at your resolution (note the screen and the text).
- Every place you did not know what to do next (the tutorial hints are supposed to cover it).
- Crashes or hangs: the terminal prints a stack trace; paste it.
- Your gut on the two flagged decisions: the §2 depot targets (BALANCE.md) and the §3 arc (career-sim.md).
- The build: did `build/windows/PerigeeFuelCo.exe` run on your Windows machine? It was exported here but never launched on Windows.
