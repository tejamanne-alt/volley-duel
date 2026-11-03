# Decisions

Each entry: the decision, the options considered, and why. Spec interpretations are marked
**[spec]**: places where the concept doc was silent or ambiguous and a default was chosen so
work could continue. The ones worth a second look by the designer are flagged **review**.

## Engineering

### D1. Build `com.duel.core` here (Dunk Duel has no core yet)
- Options: depend on Dunk Duel's Duel Core; build a new package.
- On 2026-11-02 `tejamanne-alt/dunk-duel` held only its initial commit (checked via commit
  search; direct read access was not granted to this session). Built `com.duel.core` as an
  embedded package in `Packages/` with no volleyball code, so Dunk Duel can consume it by
  git URL or local path. **review**: if Dunk Duel grows its own core first, reconcile.

### D2. Headless .NET build mirrors Unity's compiler settings
- Options: Unity batch mode only; separate .NET projects.
- No Unity editor in the cloud container, and Unity batch mode is slow for the inner loop.
  `Headless/` compiles the same sources at C# 9 / .NET Standard 2.1 with analyzers as
  errors. Unity remains the authority for the Unity-dependent assemblies.

### D3. NUnit 3 for tests
- Options: xUnit, NUnit. NUnit is what the Unity Test Framework runs, so the same test
  files run headless and as EditMode tests.

### D4. Q32.32 fixed point in a `long`, rounding half away from zero, saturating
- Options: Q48.16 (cheaper multiply, 1.5e-5 resolution), Q32.32 (2.3e-10 resolution,
  needs 128-bit intermediates), soft-float.
- Q32.32 keeps ballistic predictions accurate over hundreds of frames; range ±2.1e9 is far
  beyond court scale. Multiply/divide/sqrt use exact 128-bit integer helpers and are tested
  against BigInteger references. Saturation instead of wrap makes overflow bugs visible as
  clamped values rather than sign flips.

### D5. State hash = FNV-1a 64 over an explicit little-endian serialization
- Options: hash raw struct memory; explicit field serialization.
- Raw memory depends on layout and padding, which may differ between IL2CPP builds.
  Explicit `Write`/`Read` also gives full-state resend for desync recovery and the 2 KB
  budget check for free. A reflection test mutates every field and expects the hash to change.

### D6. Static gate scans compiled IL and metadata
- Options: Roslyn analyzer; source regex; IL/metadata scan.
- The scan (`System.Reflection.Metadata`) sees what ships, including implicit conversions
  and compiler-generated code, with no extra packages. A canary class proves each banned
  category is detected. Applied to Duel.Core.Sim, VolleyDuel.Sim, Duel.Core.AI and
  VolleyDuel.AI (AI too, so Balance Lab runs are reproducible across machines).

### D7. Data is JSON under `Assets/VolleyDuel/Resources/Data`, the single source of truth
- Options: ScriptableObjects (Unity-only, YAML, floats in the inspector); JSON text.
- JSON is diffable, loads headless and in Unity (TextAsset), and numbers are parsed from
  decimal text into Fix64 with integer math, so no float ever touches a gameplay number.
  An editor window can edit it later; the sim only sees baked immutable tables.

### D8. Original-IP gate with hashed terms
- The concept doc names its benchmark game; rule 1 bans that name from the repo. The repo
  copy of the doc (`docs/Volley_Duel_Concept.md`) is redacted, and `OriginalIpGate` scans
  all text files for the banned words, stored only as hashes.

## Simulation model

### S1. The sim is 2D: X along the court, Y up **[spec]**
- The doc defines only X (−6..+6) and Y and plays side-on like a 2.5D fighter. Depth is
  cosmetic: fighters and ball live on the X-Y plane; "out" means past an end line.

### S2. Hit-stop and slow motion live in the sim **[spec]**
- Options: presentation-only time effects; sim-owned world clock.
- Presentation-only slow motion would desync what players see from what the sim (and the
  remote phone) is doing. The sim keeps a tick counter (inputs, netcode) and a world-frame
  counter that pauses during hit-stop and advances every 2nd/3rd tick in slow motion. Ring
  windows and frame data count world frames. The input buffer ages in world frames, so a
  press during hit-stop is kept.

### S3. Every touch is a planned contact with a timing ring **[spec]**
- The sim predicts the frame T* when the ball reaches the fighter's sweet spot for the touch
  the context implies (bump, set, spike, serve). The ring is centered on T*. A press inside
  the ring commits; contact happens at T* (or at the press if it is late and the ball is
  still in reach) and is graded by the press offset: Perfect 4 frames wide (±1 per 2
  attribute points from 5), Good 10 (±1 per point), then a 6-frame Late band; earlier than
  that is a whiff with recovery, which makes mashing lose.
- Spikes commit on **release** of HIT: hold to charge (full charge 12 frames, Rafa 8),
  release to swing. Tips (FEINT) and tricks commit on press.
- Attack choice and aim lock 3 frames after the commit (the "direction and button within 3
  frames are simultaneous" rule); Arjun's Sleight locks 6 frames later.

### S4. Controls mapping **[spec] review**
- Stick X is relative to the net (positive = toward it), so the sim is mirror-symmetric.
- Touch by context: 1st touch bump, 2nd touch overhead set, 3rd touch attack; airborne HIT
  is always an attack, so attacking on 1 or 2 works. Grounded 3rd touch is a save bump over.
- Set type at set contact: stick toward the net = quick set, otherwise high set.
- Attack depth at lock: toward net = short and steep, neutral = middle, away = deep and flat.
- FEINT airborne: toward/neutral = tip just over the block, away = roll shot deep.
- Dig stance = full back tilt while defending (slow backward drift); a gentle back tilt
  still backpedals. Dig stance adds 2 Good frames to digs and removes 0.05 pressure (data).
- JUMP is a block jump when defending within block range of the net, else an approach jump.

### S5. Attribute starting values (the embedded table did not survive export) **[spec] review**
Six attributes, 1–10, total 36, a single 9 marks the mastery:

| Fighter | Power | Jump | Speed | Control | Dig | Block | Height |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Rafa "Cannon" Duarte | **9** | 7 | 5 | 3 | 5 | 7 | 1.98 m |
| Defne "Bastion" Yıldız | 7 | 6 | 4 | 5 | 5 | **9** | 1.93 m |
| Rin "Inkline" Takeda | 3 | 6 | 8 | 7 | **9** | 3 | 1.62 m |
| Arjun "Mirage" Nair | 5 | 7 | 7 | **9** | 5 | 3 | 1.84 m |

Rafa's weakest touch → Control 3; Defne's slow backpedal → Speed 4; Rin lowest power and
(with Arjun) lowest block; Arjun average power, lowest block.

### S6. Soft-counter loop (diagram did not survive export) **[spec] review**
Rafa > Arjun (heavy hitters overpower his dig) > Defne (tips behind her block, deep rolls)
> Rin (tall blockers stuff her) > Rafa (long rallies gas him). Even pairings: Rafa–Defne,
Arjun–Rin. Each edge is a mastery meeting a weakness.

### S7. Scoring details **[spec]**
- A Finisher's 2 points clamp at the set cap of 9 (8–8 then Finisher ends 9–8).
- Set 1's first server is drawn from the match RNG; afterwards whoever received first in a
  set serves first in the next.
- On a Run: three straight points arms +50% Perfect window on that player's next attack
  contact; it stays lit until they lose a rally, and the player who breaks it gains 100 Surge.
- Set point: the serve at set or match point flies at third speed until it crosses the net.
- Denial: one attempt per Finisher (the first DENY press); costs 100 Surge only on success.

## Kits

### K1. One TRICK button, context picks the trick **[spec]**
Each fighter's two offensive tricks have disjoint trigger contexts, so the button never needs
a second input: Rafa — Afterburner on the serve, Pipe Shot when airborne ≥ 2.5 m off the net;
Defne — Slide when approaching our own set, Overpass Hammer on a ball sent back tight on the
first touch; Rin — Ink Return on a dig ring in standing reach, Quick Stroke on the set ring;
Arjun — Blind Set on the set ring, Kalari Twist when airborne. Defensive tricks fire when the
ball is the opponent's (Sweep on a dig ring out of standing reach). Amped = AMP within 3 frames
of TRICK (the swipe-up), only if 100 Surge is available; otherwise the plain trick fires.

### K2. Trick interpretations **[spec] review**
- Slide "shifts the blocker's timing by 4 frames": the blocker's read ring is moved 4 frames.
  Amped hides the read until take-off.
- Commit Block "covers both spike timings": any spike contact during the 18-frame hang grades
  as Perfect; the hands still have to meet the ball spatially at the net.
- Shockwave knock-back is a pending 10-frame delay added to the hitter's next landing.
- Nothing Drops: once per rally, any ball inside Rin's dive range is reached (even when a dive
  could not get there in time) and kept alive (at least Loose).
- Ink Return counts as an attack for kill attribution; Quick Stroke carries "no power penalty"
  from the set to the spike; Amped Blind Set dumps instead if FEINT is held at contact.
- Back set (stick away at set contact) sends a high ball 3 m off the net, the Pipe Shot setup.

### K3. Finishers and Denial **[spec] review**
- Triggers: Sound Barrier on Rafa's serve; Citadel on Defne's touch after her own block;
  Ippitsu within 20 frames of Rin's Perfect dig; Maya on Arjun's second touch.
- The Denial ring is shown 18 frames before its 4–6 frame window; the window start is jittered
  ±10 frames by the match RNG so it cannot be memorized. One attempt; costs 100 only on success.
- CPU Denial timing carries 6x its tier's execution noise (cpu.json `denialTimingScale`), tuned
  so Legend-vs-Legend Finishers land 75–80% (measured 75.5% over 444 Finishers).

### K4. 1v1 geometry **[spec]**
The setter is the hitter, so passes go to 1.1 m off the net and sets travel little: quick sets
to 0.6 m, high sets to 0.9 m. Hitting from further back makes straight spikes into mid-court
impossible (the ball cannot clear the net and still drop in), which is realistic but made rallies
degenerate in the Balance Lab.

## CPU and balance

### C1. The CPU times its own swing from the visible flight, not the HUD ring
The CPU sees every ring through its reaction delay. On quick sets the spike contact comes less
than 12 frames after take-off, so a ring-driven CPU always swung late and shanked. A player
does not need the HUD ring to swing at a ball they set themselves; the CPU now predicts its own
contact from the ball as it sees it (extrapolated to now) and its own body.

### C2. Defenders anticipate hard spikes from the hitter's swing
Spikes reach the defender 8–18 frames after contact, below the 12-frame Legend reaction floor,
so no ring-reacting defender could ever dig one. The CPU now reads the visible jump and swing,
estimates when a spike at it would arrive, and pre-times the dig with 2.5× its execution noise
(cpu.json `anticipationScale`). Tips and shots elsewhere let the early press expire.

### C3. Balance status (P3 gate) — not yet passing **review**
- Options considered: tune attributes within the 36-point budget; tune derived constants (dig
  reach per Dig point, spike speeds); tune CPU personalities. Applied: dig reach 1.2 m + 0.1 per
  Dig point; Rafa Stamina cost ×1.6; aim fixes. Kept the spec's spike speeds (17 + 1 per Power).
- Finding: with near-perfect Legend execution the CPU rarely errs, so power kills decide
  matches and the defensive specialist (Rin) cannot win by outlasting. Arjun's Sleight (late
  choice lock) is a human-reading weapon the CPU barely exploits. Both are cases where CPU play is
  a weak proxy, as the spec warns; the next lever is human playtests plus CPU defensive reads.
- Latest gate run (200 matches per pairing, Legend): see SPIKES.md.

## Netcode

### N1. Inputs: one 20-byte packet per tick, timed gap resend after the ack
- Options considered: (a) cap the window at ack + 8 so the remote never sees a gap; (b) always
  send the newest 8 inputs, and resend the window after the remote's ack once those inputs have
  gone unacknowledged for longer than a round trip.
- Chose (b). (a) limits throughput to 8 frames per round trip, so it stalled every match above
  133 ms RTT. (b) costs one 20-byte packet per tick in steady state, plus at most two more while
  a gap is open. The receiver already stores inputs out of order, and confirmation advances as
  soon as the gap fills.

### N2. RTT comes from input acks, Karn-style
RTT is sampled from the first ack of each input frame. A sample is skipped when the frames it
covers needed a gap resend, or sat in the window through a stall or pause. After a 4 s dropout,
counting those samples raised the RTT estimate to about half a second, which slowed recovery to
a crawl. Resend timing and time sync both read this estimate.

### N3. Time sync from the newest window only
Each newest input packet tells the receiver where the sender is: its frame plus the input delay.
Resent windows and late, reordered packets are ignored for this. Every 30 ticks, a peer that
averaged more than one frame ahead waits one frame. Beyond the 8-frame rollback budget, the
leading peer stalls.

### N4. Soak harness and gate
`BalanceLab netsoak` plays full Legend CPU matches as two phones over `InMemoryLink`, which is
seeded and reproducible. A match counts as finished only when both phones confirm the same hash
at the first checkpoint after the final whistle. Gate: zero desyncs, every match finished
identically, rollback p95 at most 4 frames. Results are in SPIKES.md. The 30-match device soak
over real BLE still needs two phones.
