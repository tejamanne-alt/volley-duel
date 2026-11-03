# Spikes and measurements

Measured results only. Every number below can be reproduced with the command next to it.
Desktop numbers come from the cloud container: x64, 4 shared cores, .NET 8 Release. They are
not device numbers.

Last run: 2026-11-03 on branch `claude/laughing-galileo-eo41if`.

## Summary

| Gate | Result | Status |
| --- | --- | --- |
| Static determinism: no UnityEngine, float, double, Random or DateTime in Sim and AI | 0 violations | pass |
| Rules, netcode and transport tests | 118 game, 69 core, 8 gates | pass |
| Determinism: 10,000-frame golden replay | hash `0x1B1158132F78542F`, 1,590 bytes | pass (desktop); device runs pending |
| Netcode soak (simulated BLE link, 30 matches) | 0 desyncs, rollback p95 0 frames | pass |
| Resimulate 8 frames under 1 ms (Low-tier phone) | p99.9 0.071 ms on desktop | likely; needs a phone |
| Balance: pairings 45–55% | 5 of 10 pairings outside | **fail** |
| Balance: receiver wins 55–60% of rallies | 52.9% | **fail** |
| Balance: CPU tiers monotonic | each tier beats the one below 89–100% | pass |
| BLE echo iPhone–Android, both role directions | not run | needs two phones |
| Wi-Fi Aware echo | not attempted | needs the entitlement (ask first) |
| Look-dev slice frame time | not run | needs art and a mid-tier phone |

## Sim cost

`dotnet run -c Release --project Headless/VolleyDuel.BalanceLab -- bench`

| Measure | Result |
| --- | --- |
| Step (no CPU), mean over 60,000 steps | 4.12 µs |
| Allocations over 60,000 steps | 40 bytes, 0 gen-0 collections |
| Resimulate 8 frames + serialize, 2,856 windows | p50 0.014 ms · p99 0.046 ms · p99.9 0.071 ms · worst 1.79 ms |
| Serialized state | 682 bytes (budget 2,048) |

The 1.79 ms worst window is a one-off OS scheduling stall on shared cores. No collection ran,
and p99.9 is 25 times lower. A Low-tier phone running IL2CPP is perhaps 5–10 times slower than
this desktop, which puts p99.9 near 0.4–0.7 ms. That is under the 1 ms budget, but it is not
yet measured on a phone.

## Determinism

`dotnet run -c Release --project Headless/VolleyDuel.BalanceLab -- golden --write`

- Setup: Rafa vs Rin, Showcase Night, seed 20261103, Pro vs National CPU.
- Result: 10,000 frames, 89 ball contacts, score 1–2, sets 0–1.
- Final hash: `0x1B1158132F78542F`.
- The replay is `Assets/VolleyDuel/Tests/EditMode/Golden/golden-10000.replay` (1,590 bytes).
  `DeterminismTests.GoldenReplayHashesIdentically` replays it and compares the hash.
- The same test runs in the Unity Test Runner. Running it on an iPhone and an Android phone is
  the cross-device check, and it is still pending.
- Any change to sim code or sim data changes the sim version. The test then fails with an
  instruction to re-record, so re-recording is always a deliberate step.

## Netcode soak

`dotnet run -c Release --project Headless/VolleyDuel.BalanceLab -- netsoak --matches 30 [--latency ms --jitter ms --loss %]`

Each soak plays full Legend CPU-vs-CPU matches as two phones (host and guest), each with its own
rollback session, over a seeded in-memory link. A match counts as finished identically only when
both phones confirm the same hash at the first checkpoint after the final whistle.

| Link (one way) | Finished identically | Desyncs | Rollback p95 / p99 / max (all frames) | p95 depth when rolling back | Stalls | Mean RTT |
| --- | --- | --- | --- | --- | --- | --- |
| 15 ms + 0–5 ms jitter, 1% loss | 30/30 | 0 | 0 / 0 / 3 | 1 | 0 | 53 ms |
| **30 ms + 0–15 ms, 3% loss (default, BLE-like)** | **30/30** | **0** | **0 / 1 / 3** | **1** | **0** | **91 ms** |
| 45 ms + 0–30 ms, 8% loss | 30/30 | 0 | 2 / 3 / 7 | 3 | 0 | 132 ms |
| 80 ms + 0–40 ms, 15% loss | 30/30 | 0 | 4 / 5 / 8 | 6 | 554 | 212 ms |

The last profile is well past the spec's 15–45 ms each way. It degrades as designed: the
rollback budget caps at 8 frames, and the leading phone stalls for a frame at a time.

`Headless/Duel.Core.Tests` (`NetcodeTests`, `LanTransportTests`, `BleTransportTests`) and
`VolleyDuel.Tests` (`NetplayTests`) cover the following on every check:
- Desync detection and repair.
- A 4 s dropout followed by resume at full speed, and no contest after 20 s.
- Stray packets from an earlier match.
- Two volleyball sets over a lossy link, compared to an offline replay.
- A handshake plus rollback session over real UDP loopback and over the BLE loopback bridge.

## Balance Lab

`dotnet run -c Release --project Headless/VolleyDuel.BalanceLab -- gate --matches 200`
(2,000 matches in about 50 s)

Legend vs Legend, 200 matches per pairing. The percentage is how often the first fighter wins.

| | Rafa | Defne | Rin | Arjun |
| --- | --- | --- | --- | --- |
| **Rafa** | 47.0 | 46.0 | **98.0** | **83.0** |
| **Defne** | | 47.0 | **66.5** | **90.0** |
| **Rin** | | | 45.0 | **61.5** |
| **Arjun** | | | | 59.0 |

Other results:
- Receiver wins 52.9% of rallies.
- Average match length is 7.8 minutes; average rally length is 14.7 s.
- Finishers land 75.3% of the time (2,438 of 3,239).
- Contacts: 92% Perfect and 91% controlled. Legend execution is close to flawless.

Tier gate (`tiers`), where each tier plays the one below it:

| Matchup | Win rate |
| --- | --- |
| Club vs Rookie | 95.5% |
| Pro vs Club | 99.5% |
| National vs Pro | 92.5% |
| Legend vs National | 89.5% |

### What the traces show

These come from per-rally traces (`trace`, then the replay viewer) of Rafa vs Rin.

- **Rin defends too deep.** Rafa's short, steep spikes land about 1.9 m from the net in 10
  frames. Rin waits at 4.6 m, so the ball lands 3 m in front of her, beyond her dig reach and
  her dive range.
- **Rin jumps to block Rafa when her hands can't reach.** Her block tops out near 2.7 m and
  Rafa contacts at about 3.3 m. His deep spike passes over her and lands 5.3 m deep, with
  nobody back.
- **Low Control barely matters.** At Perfect timing the contact model only drops 0.06 for
  Control 3, so Rafa's spikes stay controlled.

### Experiments tried this session, all reverted

| Change | Rafa–Rin | Other pairings |
| --- | --- | --- |
| Scramble digs (higher and shorter after a dive) | no change | Rin's errors moved from digs to sets |
| Dig reach 0.18 per Dig point (was 0.10) | no change | Defne–Rin improved to 54.5%; Rin–Arjun worsened to 81.5% |
| CPU skips blocks that can't reach, and digs from mid-court | worse (100%) | Rin lost her stuffs, which are 56% of her points |

The fix has to change several levers together, and checking it needs human playtests. See
DECISIONS C3 for the options.

## Not run here

These need hardware or approvals this environment doesn't have:
- **BLE echo** between an iPhone and an Android phone at 60 Hz for 10 minutes, in both role
  directions. The bridges are written (`DuelBle.swift`, `DuelBle.kt`). The Kotlin compiles
  against the Android 14 API; the Swift could not be compiled here.
- **Wi-Fi Aware echo.** It needs `com.apple.developer.wifi-aware`, which was not requested
  (contract: ask first).
- **Look-dev slice and performance on reference phones.**
- **Opening the project in Unity 6.3.** The Unity-facing code is type-checked against API stubs
  (`Headless/UnityApiCheck`), which is not the same as a Unity build.
