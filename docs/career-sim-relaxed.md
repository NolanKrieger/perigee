# Career model (M12 gate)

Relaxed preset, seed 7, real economics through the flight harness (`tools/CareerSim`, `SimHost.ChargeLaunches`). Play time per scripted tick = game seconds ÷ the warp a player would use (hands-on flying 1×, idle physics 4×, rails 100000×, never slower than the script ran) + 120 s per launch + 60 s per mission. Orbital set-ups the scripts spawn are paid at launch cost plus $22.0k for the recovered booster they skip. Hands-on hours = time flying with throttle or input. A scripted pilot never crashes or re-flies an approach, so a real career takes longer than these hours.

| phase | step | day | play h | hands-on h | cash | score | supply trips | note |
|---|---|---|---|---|---|---|---|---|
| 1 Sounding rockets | start | 1 | 0.0 | 0.0 | $1.50M | $1.50M | 0 | Relaxed preset |
| 1 Sounding rockets | chain | 1 | 0.0 | 0.0 | $1.50M | $1.50M | 0 |  |
| 1 Sounding rockets | T1 Sounding Rocket | 1 | 0.1 | 0.0 | $1.53M | $1.53M | 0 | T1 Sounding Rocket:20 km and a safe landing |
| 1 Sounding rockets | T2 Space Is Up + T3 First Orbit (one flight) | 1 | 0.2 | 0.1 | $1.68M | $1.69M | 0 | T2 Space Is Up + T3 First Orbit (one flight): |
| 2 Reuse | T4 Comsat | 1 | 0.3 | 0.1 | $1.86M | $1.87M | 0 | T4 Comsat:client relay into a 200–300 km orbit (starter parts |
| 2 Reuse | T5 Bring It Back | 1 | 0.4 | 0.2 | $2.00M | $2.00M | 0 | T5 Bring It Back:booster flashback to the pad |
| 3 First depot | T6 Handshake | 1 | 0.5 | 0.2 | $2.15M | $2.17M | 0 | T6 Handshake:dock with the client test target |
| 3 First depot | T7 Top Up | 1 | 0.6 | 0.2 | $2.34M | $2.41M | 0 | T7 Top Up:pump 2 t into the client satellite |
| 3 First depot | T8 Gas Station | 1 | 0.7 | 0.3 | $1.64M | $1.73M | 0 | T8 Gas Station:depot in low orbit sells 10 t (tanker → depot → sale) |
| 3 First depot | supply ×3 | 8 | 1.3 | 0.6 | $1.69M | $1.77M | 3 | first paid runs after the depot opens |
| 3 First depot | T9 Touchdown | 10 | 1.4 | 0.7 | $2.13M | $3.11M | 3 | T9 Touchdown:land a probe on Vell |
| 4 Mining | T10 Dig | 11 | 1.6 | 0.7 | $2.00M | $2.99M | 3 | T10 Dig:outpost + drill on the nearest ice deposit |
| 4 Mining | grind | 345 | 12.4 | 7.2 | $2.17M | $3.17M | 58 | 55 supply trips to afford mining outpost ($2.17M) |
| 4 Mining | outpost | 351 | 12.7 | 7.3 | $167.1k | $1.58M | 58 | scan orbit, full outpost (refinery + storage) landed in the ice arc |
| 4 Mining | week | 358 | 12.7 | 7.3 | $142.6k | $1.56M | 58 | outpost producing |
| 5 Off-world pads | grind | 366 | 13.3 | 7.7 | $165.5k | $1.58M | 61 | DEAD END: a supply trip lost money ($171.3k → $165.5k); grinding cannot fund off-world pad |

## Phase ends vs the §3 arc

| phase | model ends at (play h) | §3 arc (h) |
|---|---|---|
| 1 Sounding rockets | 0.2 | 0–3 |
| 2 Reuse | 0.4 | 3–8 |
| 3 First depot | 1.4 | 8–14 |
| 4 Mining | 12.7 | 14–24 |
| 5 Off-world pads | 13.3 | 24–32 |
| 6 The network | not reached | 32–50 |

**Totals:** 13.3 play-hours modelled (7.7 hands-on), 72 missions, 74 launches (0 home relay contracts, 0 days waited for offers), 61 repeat supply trips (283 days waited for the home node's price to recover, 2 for storms or a fried depot) → **4.60 repeat supply trips per play-hour** (GDD §23 metric); tanker launches cost $7.54M, depot sales earned $3.66M, tanker refunds returned $4.82M (net $942.1k, $15.4k per trip). R&D spent $3.36M. Final cash $165.5k, peak score $3.17M, career day 366.
