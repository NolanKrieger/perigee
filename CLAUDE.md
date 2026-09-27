# Perigee Fuel Co. — project rules

2D space exploration + logistics game (Spaceflight-Simulator-style rocketry, patched conics, fuel-depot economy). Godot 4.7.2 .NET, C# on net8.0, PC/Steam, keyboard + mouse, single-player. Owner: Nolan Krieger.

## Read first, every session and after every context compaction
1. `docs/GDD.md` — the complete, decided design. Source of truth. §22 = the 52 decisions.
2. `docs/STATUS.md` — milestone table: status, evidence, open issues, next step. This is the build's memory. Update it at every milestone and before any long operation.
3. `docs/ARCHITECTURE.md` — how the code is laid out and why. Record every ordinary engineering choice here.
4. `~/.claude/workspace/tools/languages.md` → "Godot 4.7.2 .NET + .NET 8 SDK" — toolchain, headless build, screenshots, synthetic-input gotchas on this machine.
5. `docs/HANDOFF-PROMPT.md` — the full brief (bar for "done", regression flights, shipping list).

## Hard rules
- **The design is decided.** The 52 decisions in GDD §22 are Nolan's. Don't change, cut or "improve" any of them. If one is impossible or plays badly, stop and bring Nolan measured evidence plus options. Values tagged **(proposal)** are tunable; record every change and why in `docs/BALANCE.md`.
- **Build everything, then Nolan playtests** (decision #28). Every gate in GDD §21 is automated: tests, scripted flights, sims, screenshots. Don't stop for playtests between milestones.
- **Git:** `git init` locally is fine. **Never commit or push without Nolan's explicit yes.** Ask "Ready to commit?" at the end of a milestone and wait. Never force-push, never skip hooks.
- **No spending, no external accounts.** No paid services or assets. No Steam partner actions, store page or uploads. Steam achievements sit behind an interface; the app ID is config.
- **Assets:** art is vector, drawn in code (GDD §17). Any third-party file (font, CC0 placeholder audio) needs a verified OFL/CC0 licence and a line in `docs/CREDITS.md` with its source URL.
- **Never claim anything works without evidence in the same message:** test output, scripted-run result, screenshot path, measured fps. Report failures exactly.
- **Scripted flights are a test tool only.** Players never get an autopilot (decision #16).

## Machine gotchas (CachyOS)
- Login shell is fish; use `bash` for scripts and heredocs.
- Sandboxed Bash can't see the real system: run `godot`, `dotnet` and screenshot/export commands non-sandboxed.
- No Xvfb: screenshots need a real window. The auto-tiler resizes new windows, so set the window size explicitly (`--screenshot` mode fixes 1920×1080) or capture from a viewport of known size.
- **Never `pkill -f` / `pgrep -f` a pattern that appears in your own command line** (it kills your own shell). Collect PIDs, then `kill` them.
- Never restart system services. Don't kill processes you didn't start.

## Verify before claiming (the checklist)
- `dotnet test tests/Sim.Tests` green.
- `godot --headless --path . -- --selftest --save=<tmp>` PASS.
- Regression flights: `dotnet run --project tools/Flight -- scripts/<name>.flight` (headless) and `godot --path . -- --script=scripts/<name>.flight` (real input path).
- Screenshots via `godot --path . -- --screenshot=docs/screenshots/M<n>/<name>.png ...`, then look at them (Read tool) for alignment, contrast, clipping and §17 palette.
- Performance claims: numbers from `-- --bench`.

## Talking to Nolan
No fluff. 1–5 short lines, exact paths and numbers, detail in files. He has no Godot experience: when an engine concept matters to a decision he must make, explain it in one sentence.

## When to stop and ask
A decided rule looks unworkable with measured evidence · a commit or push · anything costing money or needing an account/credentials · any change that widens scope beyond the GDD. Otherwise keep going and record choices in `docs/ARCHITECTURE.md`.
