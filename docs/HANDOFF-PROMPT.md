You are taking over **Perigee Fuel Co.**, a 2D space exploration and logistics game for Nolan Krieger. Your job is to build the entire game described in its design doc, from an empty folder to a polished release candidate, working autonomously through every milestone.

## Read first (in this order)
1. `~/perigee/docs/GDD.md`: the complete, decided design. This is the source of truth.
2. `~/.claude/workspace/tools/languages.md`, section "Godot 4.7.2 .NET + .NET 8 SDK": the toolchain, working C# project shape, headless build, screenshots and synthetic-input gotchas on this machine.

## What the game is (one paragraph; the GDD has everything)
Spaceflight Simulator-style 2D rocketry with a logistics economy. You play a career only, flying uncrewed probes in a procedurally generated star system with patched-conic orbits. Rockets are built from fixed parts. You land boosters by hand for refunds (using "booster flashback"), dock and pump fuel into orbital depots, then mine ice/CO₂ off-world, refine methalox and build off-world launch pads. There's a supply & demand market, contracts (the early ones are the tutorial), debris and solar storms, and an instant game over at $0 with a 10-day grace. Flat vector art drawn in code. Godot 4.7.2 .NET / C#, PC/Steam, keyboard + mouse, single-player.

## Hard rules
- **The design is decided.** The 52 decisions in GDD §22 are Nolan's. Don't change, cut or "improve" any of them. If one turns out impossible or plays badly, stop and bring Nolan evidence plus options. Values tagged **(proposal)** are yours to tune, as long as you record each change and why in `docs/BALANCE.md`.
- **Build everything, then Nolan playtests** (decision #28). Don't stop for playtests between milestones. Every gate in GDD §21 is automated: tests, scripted flights, sims, screenshots.
- **Git:** `git init` locally is fine. **Never commit or push without Nolan's explicit yes.** Ask "Ready to commit?" at sensible points (end of each milestone) and wait. Never force-push or skip hooks.
- **No spending and no external accounts.** No paid services or assets. No Steam partner actions, store page or uploads. Steam achievements go behind an interface; the real app ID needs Nolan's Steamworks account, so leave it as config.
- **Assets:** art is vector, drawn in code (GDD §17). Any third-party file (font, CC0 placeholder music or SFX) must have a verified license (OFL/CC0) and be listed in `docs/CREDITS.md` with its source URL.
- **Never claim anything works without evidence** in the same message: test output, scripted-run result, screenshot path or measured fps. If you're unsure, check first. Report failures exactly.
- **Machine gotchas (CachyOS):**
  - The login shell is fish; use `bash` for scripts.
  - Sandboxed Bash can't see the real system, so run `godot` and screenshot/export commands non-sandboxed.
  - There's no Xvfb, so screenshots need a real window. The desktop's auto-tiler resizes new windows, so set the window size explicitly, or capture from a viewport of known size.
  - **Never `pkill -f`/`pgrep -f` a pattern that appears in your own command line** (it kills your own shell). Collect PIDs, then `kill` them.
  - Never restart system services.
- **Talking to Nolan:** no fluff. Replies are 1–5 short lines with exact paths and numbers. Put detail in files. He has no Godot experience, so when an engine concept matters to a decision he has to make, explain it in a sentence.

## First actions
1. Create `~/perigee/CLAUDE.md` containing these hard rules plus "read docs/GDD.md and docs/STATUS.md first", so they survive context compaction.
2. Create `docs/STATUS.md`, a living milestone table (status, evidence, open issues, next step). Update it at every milestone and before any long operation. It is your memory: after a compaction, reread `CLAUDE.md`, `STATUS.md` and the GDD section you're working in before touching code.
3. Build M0 from GDD §21 using the project shape in `tools/languages.md`: `Perigee.csproj`, a pure-C# `src/Sim` with no Godot dependency, `tests/Sim.Tests` (xUnit) and the `game/` Godot layer.

## Build order
GDD §21, M0 → M13, in order. Each milestone is done only when its "Done when" gate passes and the evidence is in STATUS.md. You may parallelize independent work with subagents, but you own integration and verification.

## "Perfect and polished": the bar you must hit before calling it done
**Simulation and correctness**
- Every gating test in GDD §20 exists and passes. On top of that, there's unit coverage for every Sim system (orbits, SOI, craft/breakup, aero/heat, docking, economy, market, contracts, mining, generator, debris, storms, save/load).
- **Scripted-flight harness:** a `--script=<file>` mode that replays timestamped input timelines through the real input path. The rest of the game is tested through it. Use the synthetic-input notes in `tools/languages.md`.
  - Regression flights with pass/fail criteria:
    - Launch → orbit → deorbit → landing
    - Booster-flashback pad landing
    - Rendezvous + dock + pump
    - Landing on the nearest body
    - The tanker → depot → sale loop
    - Outpost setup
    - Off-world pad build + launch
    - Storm shelter in a shadow cone
    - Tutorial contracts T1–T10
  - Scripted flights are a **test tool only**. Players never get an autopilot (decision #16).
- **Generator:** 1,000-seed validation, including the "ice + ore within ≈ 2,500 m/s" guarantee.
- **Economy:**
  - The §2 depot balance targets hold.
  - A simulated 50-hour career matches the §3 arc.
  - There are no infinite-money exploits, no unrecoverable soft-locks short of bankruptcy, and no contract that can't be completed.
  - Report the repeat-supply-trips-per-hour metric from the §23 design risk.

**Game-feel and presentation**
- Every screen in GDD §16 is fully implemented: no placeholder text, no dead buttons, readable at 1920×1080 and 1600×900.
- Also: settings (volume per bus, resolution/fullscreen, UI scale, key rebinding), pause, confirmations for irreversible spends, and tooltips for every part and HUD readout.
- **Visual polish:** at the end of every milestone, capture screenshots of each new screen and key flight moments to `docs/screenshots/M<n>/`. Review them yourself for alignment, contrast, clipping and consistency with the §17 palette, and fix what you find before moving on.
  - Flight moments: launch, max-Q, staging, reentry glow, landing, docking, the map at every zoom band.
  - Verify 2D MSAA on the Mobile renderer on this RTX 2070 SUPER. If it fails, report it and pick the best working alternative.
- **Audio** per GDD §18: adaptive music layers, engine sound that scales with air density, and the full SFX list. Placeholder music is CC0 and credited.
- **Saves:** atomic writes (temp file + rename), a save-format version with migration, and a load-back round-trip test for every system.
- **Performance:** measure the GDD §20 targets in-engine on this machine (a bench flag in the style of `tools/languages.md`) and record the numbers. Fix before release if any target is missed.

**Shipping**
- Linux and Windows export presets. Install the Godot 4.7.2 mono export templates (free, from the official Godot download) if they're missing. Exported builds go in `~/perigee/build/<platform>/`, and the Linux build is verified to launch and run the launch-to-orbit script.
- Steam: Steamworks.NET wired behind an interface. The game must run without Steam present. Achievement IDs from GDD §19 live in config.
- Docs:
  - `README.md` (build/run/test/export)
  - `docs/ARCHITECTURE.md`
  - `docs/BALANCE.md` (every tuned number and why)
  - `docs/CREDITS.md`
  - `docs/PLAYTEST.md` (Nolan's first-playtest checklist: what to try, in what order, and what feedback you need)

## When to stop and ask Nolan
- A decided rule looks unworkable or unfun, and you have measured evidence.
- A commit or push.
- Anything that costs money or needs an account or credentials.
- Any change that would widen scope beyond the GDD.

Otherwise, keep going. Don't ask permission for ordinary engineering choices; make them and record them in `docs/ARCHITECTURE.md`.

## Finish line
All of M0–M13 green in STATUS.md with evidence, the exported Linux build at `~/perigee/build/linux/`, and `docs/PLAYTEST.md` ready. Then send Nolan a short message: where the build is, how to launch it, and the three things you most want him to test.
