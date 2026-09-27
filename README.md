# Perigee Fuel Co.

2D rocketry and fuel-logistics game (Spaceflight-Simulator-style flight, patched-conic orbits, a supply-and-demand fuel economy). Godot 4.7.2 .NET / C#, PC, keyboard + mouse, single-player career. The design is `docs/GDD.md`; the state of the build is `docs/STATUS.md`.

## Layout

| Path | What |
|---|---|
| `src/Sim/` | The whole simulation as a pure C# library (no Godot): orbits, craft, aero/heat, docking, economy, contracts, mining, pads, hazards, generator, save. |
| `game/` | The Godot layer: rendering, input, HUD, builder, screens, audio. Reads the sim and sends it commands. |
| `tests/Sim.Tests/` | xUnit suite for the sim (runs headless). |
| `tools/Flight/` | Headless runner for `.flight` scripts. `tools/CareerSim/` plays a whole career through the harness with real economics. `tools/Probe/` is scratch. |
| `scripts/` | Regression flights (`*.flight`) driven through the real input path; `scripts/templates/` are career-model templates. |
| `docs/` | GDD, STATUS, ARCHITECTURE, BALANCE, CREDITS, PLAYTEST, career-sim, screenshots per milestone. |

## Build and run

```
dotnet build Perigee.csproj          # game + sim (the Godot wrapper needs DOTNET_ROOT; ~/.local/bin/godot sets it)
godot --path .                        # title screen (New Career / Continue / Settings)
godot --path . -- --demo              # dev demo world on the pad (also --seed=N, --design=<name>, --zoom, --time, --warp)
```

Flight keys: `Shift`/`Ctrl` throttle up/down, `Z` full, `X` cut, `A/D` (or arrows) rotate, `Space` stage, `R` RCS, `IJKL` translate, `G` legs, `P` chutes, `,`/`.` warp down/up, `/` warp reset, `Tab` cycle craft, `Enter` confirm (fly the held booster), `Backspace` skip the booster, `U` undock, `M` map, `B` builder, `C` contracts, `H` HQ, `F` transfer panel, `Esc` pause. The flight actions are rebindable in Settings.

## Test

```
dotnet test tests/Sim.Tests                                            # sim suite (the 1,000-seed generator test takes about a minute)
godot --headless --path . -- --selftest --save=/tmp/perigee-selftest.json   # drives the real game through its input/UI paths; exit 0 = all checks passed
dotnet run --project tools/Flight -- scripts/<name>.flight              # a regression flight, headless
godot --path . -- --script=scripts/<name>.flight --shots=docs/screenshots/Mn --fast   # the same flight through Godot, with screenshots (needs a window)
godot --path . -- --bench                                               # GDD §20 performance targets on this machine
dotnet run --project tools/CareerSim -- --hours=50                      # the career model → docs/career-sim.md
```

Regression flights: `sounding-hop`, `launch-orbit-land`, `booster-flashback`, `rendezvous-dock`, `tutorial-t1-t10`, `outpost-setup`, `offworld-pad`, `storm-shelter`, `debris-cleanup`, `supply-loop` (needs a depot in orbit; the career model runs it after T8).

## Export

```
tools/build.sh        # Linux + Windows release builds into build/<platform>/ and zips into build/dist/
```

Needs the Godot 4.7.2 mono export templates in `~/.local/share/godot/export_templates/4.7.2.stable.mono/` and `Perigee.sln` next to the project (Godot's .NET exporter skips the C# payload without it). The exported Linux binary accepts the same test flags: `build/linux/PerigeeFuelCo.x86_64 --headless -- --script=scripts/launch-orbit-land.flight --fast --quit` (`--fast` steps the script 200 ticks per frame; `--quit` exits at the end, 0 = PASS).

## Saves and settings

`user://` is `~/.local/share/godot/app_userdata/Perigee Fuel Co/`: `profile.json` (settings, key bindings, personal bests) and `careers/<slug>.pfc` (one autosaving JSON+gzip save per career, atomic writes with a `.bak`). `--profile=<file>` and `--careers=<dir>` redirect both for tests.
