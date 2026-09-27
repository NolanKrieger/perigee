# Career model (M12 gate)

Standard preset, seed 7, real economics through the flight harness (`tools/CareerSim`, `SimHost.ChargeLaunches`). Play time per scripted tick = game seconds ÷ the warp a player would use (hands-on flying 1×, idle physics 4×, rails 100000×, never slower than the script ran) + 120 s per launch + 60 s per mission. Orbital set-ups the scripts spawn are paid at launch cost plus $22.0k for the recovered booster they skip. Hands-on hours = time flying with throttle or input. A scripted pilot never crashes or re-flies an approach, so a real career takes longer than these hours.

| phase | step | day | play h | hands-on h | cash | score | supply trips | note |
|---|---|---|---|---|---|---|---|---|
| 1 Sounding rockets | start | 1 | 0.0 | 0.0 | $800.0k | $800.0k | 0 | Standard preset |
| 1 Sounding rockets | chain | 1 | 0.0 | 0.0 | $800.0k | $800.0k | 0 |  |
| 1 Sounding rockets | T1 Sounding Rocket | 1 | 0.1 | 0.0 | $813.3k | $813.3k | 0 | T1 Sounding Rocket:20 km and a safe landing |
| 1 Sounding rockets | T2 Space Is Up + T3 First Orbit (one flight) | 1 | 0.2 | 0.1 | $901.0k | $902.2k | 0 | T2 Space Is Up + T3 First Orbit (one flight): |
| 2 Reuse | T4 Comsat | 1 | 0.3 | 0.1 | $1.02M | $1.02M | 0 | T4 Comsat:client relay into a 200–300 km orbit (starter parts |
| 2 Reuse | T5 Bring It Back | 1 | 0.4 | 0.2 | $1.09M | $1.09M | 0 | T5 Bring It Back:booster flashback to the pad |
| 3 First depot | T6 Handshake | 1 | 0.5 | 0.2 | $1.16M | $1.17M | 0 | T6 Handshake:dock with the client test target |
| 3 First depot | T7 Top Up | 1 | 0.6 | 0.2 | $1.29M | $1.35M | 0 | T7 Top Up:pump 2 t into the client satellite |
| 3 First depot | grind | 12 | 1.0 | 0.4 | $1.53M | $1.60M | 0 | 4 home relay contracts (7 days waiting for offers) to afford T8 Gas Station ($1.51M) |
| 3 First depot | T8 Gas Station | 12 | 1.2 | 0.5 | $388.4k | $486.5k | 0 | T8 Gas Station:depot in low orbit sells 10 t (tanker → depot → sale) |
| 3 First depot | supply ×3 | 39 | 1.8 | 0.8 | $408.0k | $499.4k | 3 | first paid runs after the depot opens |
| 3 First depot | T9 Touchdown | 42 | 1.9 | 0.9 | $741.4k | $1.72M | 3 | T9 Touchdown:land a probe on Vell |
| 4 Mining | grind | 1458 | 27.5 | 15.1 | $1.17M | $2.16M | 123 | STOPPED: 120 supply trips did not fund T10 Dig ($1.17M of $1.17M; $3587 per trip → 0 more trips) |

## Phase ends vs the §3 arc

| phase | model ends at (play h) | §3 arc (h) |
|---|---|---|
| 1 Sounding rockets | 0.2 | 0–3 |
| 2 Reuse | 0.4 | 3–8 |
| 3 First depot | 1.9 | 8–14 |
| 4 Mining | 27.5 | 14–24 |
| 5 Off-world pads | not reached | 24–32 |
| 6 The network | not reached | 32–50 |

**Totals:** 27.5 play-hours modelled (15.1 hands-on), 136 missions, 136 launches (4 home relay contracts, 7 days waited for offers), 123 repeat supply trips (1275 days waited for the home node's price to recover, 21 for storms or a fried depot) → **4.48 repeat supply trips per play-hour** (GDD §23 metric); tanker launches cost $18.79M, depot sales earned $7.73M, tanker refunds returned $12.95M (net $1.89M, $15.4k per trip). R&D spent $1.40M. Final cash $1.17M, peak score $2.16M, career day 1458.
