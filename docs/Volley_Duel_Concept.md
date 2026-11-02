> Repo copy of the concept and build prompt (Nov 3, 2026). Per contract rule 1, the benchmark
> game and its developer are not named in this repository; those mentions are redacted.
> Diagrams and embedded tables did not survive the Markdown export; see DECISIONS.md for
> the values chosen where the export dropped them.

# Volley Duel — Game Concept & Build Prompt

Nov 3, 2026 · @Teja Manne

## The pitch

Volley Duel is a 1v1 volleyball fighting game for iPhone and Android: two players, one net, one ball, best of three sets. It plays side-on in landscape like a 2.5D fighter, and every rally turns on a read at the net — spike or tip against block or dig.

The quality bar is the feel of a modern AAA fighter (the benchmark game): heavy impacts, cinematic finishers, venues that react. Everything here is original; that game is a benchmark, never a source of characters, names or UI. Volley Duel is a sibling of Dunk Duel and should share its engine core, so the second game costs a fraction of the first.

- **Every rally is a duel.** A two-choice mind game at the net, layered with timing rings and two meters: learnable in a minute, deep enough for rivalries.
- **Every kill lands like a knockout.** Hit-stop, camera punches, slow motion and crowd eruptions make stuff blocks, floor saves and cannon spikes feel physical.
- **Every venue is alive.** Twelve courts across arenas, schools, streets and beaches, with crowds, weather and set-piece reactions, built for a fixed side camera so phones can afford the detail.

| Spec | Choice |
| --- | --- |
| Genre | 1v1 volleyball fighting game, 2.5D side view, landscape |
| Platforms | iOS and Android, cross-play over Bluetooth |
| Engine and art | Unity 6.3 LTS with URP; models, rigs and animation in Blender |
| Players | 2 over Bluetooth, or 1 against the CPU at five difficulty tiers |
| Roster | 4 fighters: 2 men, 2 women |
| Courts | 12: 3 arena, 3 school, 3 street, 3 beach |
| Match | Best of 3 sets; a set is first to 7, win by 2, capped at 9; about 5–7 minutes |
| Shared with Dunk Duel | Deterministic sim framework, rollback netcode, BLE transport, CPU tiers, Balance Lab |

## Engine: Unity 6.3 LTS, not Unreal

Unity is enough for this game, and it is the better fit. The benchmark game's look comes from art direction, lighting and animation craft; with a fixed side camera and two fighters on screen, either engine can afford it, so Unreal would not raise the ceiling.

- **The look is authored, not switched on.** Its developer built that game on its own in-house engine, so no engine ships its look. Sculpting, exaggerated anatomy, hand-tuned fighter lights, hit-stop and effects do the work, and URP with custom shaders can carry all of it.
- **Unreal's flagship features don't reach phones.** Lumen is experimental, only on high-end Android through the heavier desktop renderer, and doesn't work on iOS ([Epic, UE 5.8 docs](https://dev.epicgames.com/documentation/unreal-engine/using-lumen-global-illumination-on-mobile-in-unreal-engine?lang=en-US)). Nanite isn't supported by the mobile renderer ([Arm, March 2026](https://developer.arm.com/community/arm-community-blogs/b/mobile-graphics-and-gaming-blog/posts/mali-and-unreal-engine-s-nanite-enabling-the-future-of-mobile-graphics)).
- **Mobile delivery favors Unity.** A smaller base app, broad reach on mid-range Android phones, and mature tooling for stores, ads and native plugins. The Bluetooth layer is native Swift and Kotlin in either engine.
- **Agent-friendly.** Unity C# is plain text a coding agent can read, write and test; Unreal Blueprints are binary assets and C++ rebuilds are slow. Unity's MCP server connects Claude Code to the Editor; it is in open beta and needs a Unity AI trial or subscription ([Unity, May 2026](https://unity.com/blog/unity-ai-mcp-how-to-get-started)).
- **Fits the current machine.** Unity runs comfortably on the 16 GB M1 MacBook Air; Unreal's editor, shader compiles and large installs would strain it and its 256 GB disk.
- **Stable and affordable.** Unity 6.3 is the current LTS, supported until December 2027 ([Unity](https://unity.com/releases/unity-6/support)). Personal is free up to $200,000 in revenue and funding; Pro is $2,310 per seat per year above that ([Unity](https://unity.com/products/pricing-updates)).

What "benchmark quality" honestly means on a phone: the same art direction, lighting, effects and camera language at phone scale, not console polygon and texture budgets. A full-quality vertical slice on a mid-range phone is the go/no-go test, before any art production.

Revisit Unreal only if consoles or PC become the lead platform. The deterministic sim and the Blender assets port to either engine.

## How a rally plays

A match is best of three sets on a compact duel court, and a set is first to 7, win by 2, capped at 9. The core is one read at the net — spike or tip against block or dig — sharpened by timing rings, two meters and cinematic finishers.

### Court and rules

- **Duel Court:** 12 m long, 6 m a side, with the net at 2.35 m, between the men's 2.43 m and women's 2.24 m regulation heights ([WA Government, FIVB rules](https://dlgsc.wa.gov.au/sport-and-recreation/sports-dimensions-guide/volleyball)). A regulation beach court is 16 m long; the shorter court keeps both fighters large on screen.
- **Rally scoring:** every rally scores, and the rally winner serves next. Tune the receiver to win 55–60% of rallies, so serving runs are rare and earned.
- **Three touches a side:** receive, set, attack. Attacking on the first or second touch is legal and is a weapon. A block touch is free.
- **Faults:** ball out, net touch, a fourth touch, a foot across the center line, a serve foot fault.
- **Sets:** at 8–8 the next point wins. Whoever received first in one set serves first in the next.
- Surge carries over between sets; Stamina resets each set.

### Controls

Landscape touch: left thumb on a floating stick, right thumb on buttons that change meaning with the phase of play.

| Control | Your ball (attacking) | Their ball (defending) |
| --- | --- | --- |
| Stick | Move; the direction at contact aims: toward the net is short and steep, away is deep and flat | Move; hold back to sink into a dig stance |
| HIT | Bump, set or spike by context, timed on a ring; hold at the top of a jump to charge a spike | Dig, timed on a ring; with the stick toward an out-of-reach ball it becomes a dive |
| JUMP | Approach jump for a spike or a jump serve | Block jump at the net |
| FEINT | Tip over the block, or dump on the second touch | Fake block: a hop that baits a tip, then drop back |
| TRICK | Your fighter's two offensive Signature Tricks; swipe up from the button for the Amped version | Your fighter's defensive Trick |
| FINISHER | Lights up at full Surge | Becomes DENY during the opponent's Finisher |

Controllers (MFi, Xbox, DualSense) are fully supported and remappable. Simple mode auto-positions the receive, widens the rings, shows a landing ring and reduces Surge gain.

### The Read

At the attack, the hitter commits to a spike or a tip and the defender commits to a block or a dig. Each choice beats one answer and loses to the other.

| Attacker ↓ / Defender → | Block: jump at the net | Dig: stay back |
| --- | --- | --- |
| Spike | Timing battle: a Perfect block stuffs it, a Good one softens it into a free ball, a late one gets tooled | Power against reflex: a Perfect dig keeps control, a Good one keeps it alive but loose |
| Tip | Drops behind the airborne blocker: point | Free ball: an easy pick-up, and the defender gains Surge |

Three more layers keep it from being a coin flip:

- **Sets shape the menu.** A quick low set gives the blocker 8 fewer frames to read but caps spike power. A high set adds power and options but telegraphs. A missed set ring leaves only a weak hit or a tip.
- **Feints bait commitments.** Fake spikes, fake sets and fake blocks each have a bait window. Commit inside it and you bite: you lose Stamina and land late.
- **Depth decides what's live.** Steep spikes land short; flat drives and roll shots land deep. A defender cheating to the net is open deep, and one sitting deep is open short.

### Meters

- **Surge, 3 bars.** Earned by winning reads and scoring with style: aces, kills, stuff blocks, Perfect digs, floor saves, read tips. 1 bar buys an Amped Trick or a Denial; 3 bars buy a Signature Finisher.
- **Stamina.** Drained by jumps, dives, jump serves and biting on feints; it refills while grounded and at each dead ball. Empty means Gassed: lower jumps and slower dives until it recovers.
- **On a Run.** Three straight points ignite your scoreboard and widen your next attack's Perfect window. Lose the next rally and the opponent gains a full Surge bar.

### Signature Finishers and Denial

A Finisher costs 3 Surge, fires only from that fighter's trigger moment, and plays a 2–3 second cinematic that scores 2 points. It is the only time the camera leaves the side view.

A defender holding at least 1 Surge bar gets one Denial timing ring: hit it and the Finisher is dug up, and the rally continues as a free ball on the defender's side. Tune Finishers to land 75–80% of the time.

### The feel

Every highlight has a fixed impact recipe. Hit-stop is freeze frames at 60 fps; the values are starting points for playtests.

| Moment | Hit-stop | Camera and time | Callout |
| --- | --- | --- | --- |
| Kill | 8 frames | Punch-in on the impact | KILL! |
| Stuff block | 12 frames | Snap zoom on the hands, half speed for 12 frames | STUFFED! |
| Ace | 10 frames | Push-in on the landing spot | ACE! |
| Floor save | 6 frames | Low tilt at floor level | PANCAKE! |
| Perfect dig | 4 frames | Small nudge | DUG! |
| Tooled block | 6 frames | Whip pan following the ball off the hands | TOOLED! |
| Read tip | 4 frames | Quick zoom on the defender | READ IT! |
| Joust above the net | 10 frames | Both frozen in the air, then a snap | JOUST! |
| Denial | 12 frames | Hard cut back to the side view | DENIED! |
| Set point | None | Third speed while the ball is in flight | SET POINT |

Small details sell it: sand sprays and footprints stay, sweat sheen builds across a match, the net cord shudders on hard spikes, and crowd intensity tracks the score margin (Stage Heat).

## The roster

Four original fighters, two men and two women, each built around one mastery on an equal 36-point attribute budget. Soft counters form a loop, and no matchup should be worse than 45–55.

&#91;embedded content: design starting values · 4 fighters × 6 attributes\]

Budget rule: six attributes on a 1–10 scale, each fighter totals 36, and the single 9 marks the mastery. Power sets spike and serve speed, Jump sets reach and air time, Speed sets court coverage, Control sets set and touch accuracy, and Dig and Block set those timing rings. These are starting values; the Balance Lab sets the final ones.

### Rafa "Cannon" Duarte — the power hitter

A Rio sand-court legend, 1.98 m, who plays barefoot everywhere, even on hardwood. Broad and loud, with sun-bleached curls in a top knot, an emerald tank with a gold wave print and black board shorts. His effects are supersonic: vapor cones, shock rings and heat streaks off the ball.

- **Mastery, Heavy Ball:** any dig short of Perfect against his spikes and jump serves is shanked, and every dig against them costs Stamina. His charged spikes reach full power in 8 frames instead of 12.
- **Afterburner:** a jump serve at top speed. Amped: the ball dives late and shrinks the receive ring by 3 frames.
- **Pipe Shot:** a spike from deep in his own court that lands on the end line, too far back to block cleanly. Amped: it can't be stuffed.
- **Shockwave Block (defense):** a slam block whose shock ring knocks the hitter back on landing, delaying their next action by 10 frames. Amped: a Perfect block also drains 25 of their Stamina.
- **Finisher, Sound Barrier (on his serve):** a sky-high toss, a vapor cone as he connects, and a shock ring that flattens the sand and rattles the stands.
- **Weakness:** the slowest landing recovery, the weakest touch and the hungriest Stamina; long rallies gas him.

### Defne "Bastion" Yıldız — the wall

An Istanbul club captain, 1.93 m, who treats the net as her property line. A high braid wrapped in gold cuffs, sleeves printed with cobalt Iznik tulips, mosaic knee pads and a captain's armband. Her effects are architectural: cobalt and turquoise tiles that snap into shields and shatter on impact.

- **Mastery, Fortress:** her block reaches 20% wider, and her Good blocks stuff the ball where everyone else's only soften it.
- **Slide:** a one-foot approach that drifts into the net mid-air and hits from point-blank, shifting the blocker's timing by 4 frames. Amped: the drift is longer and unreadable until takeoff.
- **Overpass Hammer:** she kills any ball sent back tight to the net on the first touch. Amped: she wins every joust above the net.
- **Commit Block (defense):** she leaps early and holds her hands over the net for 18 frames, covering both spike timings, but a tip behind her is unstoppable. Amped: she lands straight into a dig stance.
- **Finisher, Citadel (on the touch after her own block):** a mosaic wall rises behind the net; she rises with it and drives the ball straight down as the tiles crash over the far court.
- **Weakness:** a slow backpedal and an average dig; deep roll shots and tips behind her block beat her.

### Rin "Inkline" Takeda — the libero

A Kyoto calligraphy prodigy turned libero, 1.62 m, who treats every dig as a brushstroke. A low ponytail tied with a vermilion cord, a cream libero jersey brushed with black calligraphy, knee-high socks and wrist tape stamped with a red seal. Her effects are sumi-e ink: brush trails on dives, ink blooms on digs and a seal stamp on her best plays.

- **Mastery, Nothing Drops:** the widest dig window, plus one floor save per rally on any ball inside her dive range.
- **Ink Return:** she digs a hard spike straight back over the net and catches a hitter still landing. Amped: it drops within 1 m of the net and can't be blocked.
- **Quick Stroke:** a low self-set into an instant attack; the blocker gets 8 fewer frames to read, with no power penalty. Amped: she switches short or deep on the last frame.
- **Sweep (defense):** a 3 m sliding dive that reaches balls no one else can. Amped: she pops straight up, and the dig counts as Good at worst.
- **Finisher, Ippitsu (on a Perfect dig):** dig, set and kill in one unbroken stroke while the screen washes into an ink painting, a giant brushstroke trailing the ball into the floor.
- **Weakness:** the lowest power and block; her attacks are diggable, and tall blockers stuff her.

### Arjun "Mirage" Nair — the trickster

A Kozhikode village-tournament legend, 1.84 m, trained in Kalaripayattu before he ever touched a ball. A curly undercut, a cream tee with a gold kasavu border, a brass bangle and a grin aimed at the crowd. His effects are heat mirages: shimmering decoy outlines and brass-lamp flame flickers.

- **Mastery, Sleight:** his attack choice locks 6 frames later than anyone else's, so he can switch spike, tip or roll mid-swing. His self-sets are the most accurate in the game.
- **Blind Set:** a no-look set that hides its height and tempo until it leaves his hands, so the blocker can't pre-time. Amped: he can dump it over instead, decided on the last frame.
- **Kalari Twist:** a mid-air body twist that turns a deep swing into a steep, short spike. Amped: a heat-mirage decoy shows the wrong flight for the first 8 frames.
- **Decoy (defense):** a mirage of him leaps to block while he stays back to dig; it fades after 10 frames. Amped: the mirage lasts the whole attack.
- **Finisher, Maya (on his second touch):** three heat-mirage copies rise and all three swing; only one connects, and the camera spins to find the ball as it lands.
- **Weakness:** average power and the lowest block; heavy hitters overpower his dig.

### Keeping it balanced

&#91;embedded content: soft-counter loop · 4 fighters, 2 even pairings\]

Each edge is one fighter's mastery meeting another's weakness, so the loop survives number tuning.

- **Every tool has an answer:** each Trick loses to one side of the Read, every Amped move costs 1 Surge, and every Finisher can be Denied.
- **Data, not code:** every number lives in tunable data assets.
- **Balance Lab:** each build runs thousands of CPU-vs-CPU matches per pairing at the top tier; any pairing outside 45–55% fails the build. Human playtests confirm, since CPU play is only a proxy.

## The courts

Twelve courts in four themes, from village streets to a world final. Each has a living background, three crowd states driven by Stage Heat, and one set-piece reaction to Signature Finishers.

The brief named a professional basketball court, so the arena theme reads that as pro arenas, and Showcase Night is a real basketball arena rigged for one night of volleyball.

### Arena

| Court | Look and life | Finisher moment |
| --- | --- | --- |
| Showcase Night | A pro basketball arena rigged for one volleyball night: the key and three-point arcs still show under fresh court tape, a center-hung jumbotron, LED ribbon boards, a dance team, courtside photographers | The arena blacks out, spotlights converge, and the ribbon boards run the fighter's motif |
| The Cauldron | A European volleyball palace with steep stands of ultras: drums, giant flags, colored smoke, a hot-orange sport floor | A full-stand tifo unfurls with the fighter's emblem |
| Skydome Finals | A retractable-roof world-finals stadium in a neon metropolis: holographic court lines, drone cameras, a glass skyline beyond the upper deck | The roof splits open and fireworks bloom over the court |

### School

| Court | Look and life | Finisher moment |
| --- | --- | --- |
| Ouendan Gym | A Japanese high-school gym on tournament day: polished wood, a stage at one end, hand-lettered banners, a cheer squad with taiko drums and giant flags | The taiko line hits in unison and the flags whip across the stands |
| Rooftop Court | A school rooftop in a dense harbor city at sunset: chain-link fence, water tanks, laundry on neighboring balconies, a jet on low approach every minute | A jet roars overhead on cue and its landing lights sweep the court |
| Alpine Academy | A boarding school's snow court at bright noon: packed snow, fir trees, a chalet dorm, students with thermoses, breath clouds in the cold | The chalet roof dumps its snow in slow motion |

### Street

| Court | Look and life | Finisher moment |
| --- | --- | --- |
| Floodlight Village | A Kerala village tournament at night: a mud court ringed by bamboo-and-thatch galleries, floodlights on bamboo poles, coconut palms, a chenda drum troupe | The drums hit their crescendo and festival fireworks light the palms |
| Marigold Street | A cobbled street on the Day of the Dead: a net strung from a bakery to a balcony, papel picado overhead, candlelit altars, a brass band, petals underfoot | Thousands of marigold petals lift off the street and spiral along the ball's path |
| The Overpass | A concrete court under an elevated highway: floor-to-ceiling graffiti murals, sodium lights, a boombox crew, trucks rumbling overhead | The murals come alive, spray paint bursting in the fighter's colors and dripping down the pillars |

### Beach

| Court | Look and life | Finisher moment |
| --- | --- | --- |
| Mosaic Promenade | A city beach at golden hour: a wave-patterned stone promenade, kiosks, footvolley kids, a samba drum line, a granite peak on the horizon | The drum line hits its break and confetti cannons fire from the kiosks |
| Black Sand Coast | A volcanic black-sand beach at dusk: basalt columns, heavy surf, a smoking volcano with lava glowing on its slopes | The volcano erupts on the horizon and lava fountains turn the sky red |
| Glow Lagoon | A tropical lagoon at night: bioluminescent surf that glows blue under every step, lanterns, overwater bungalows, a fire-dancer troupe | A wave breaks along the whole shore and the glow flares bright |

Rules for every court:

- In Duel rules every court plays the same: identical dimensions, cosmetic only. An optional Local Conditions toggle for casual play adds sand drag on beaches, slide on snow and a light beach crosswind.
- Crowds have three states (calm, hyped, frenzy) driven by Stage Heat: the score margin plus recent highlights.
- Stage palettes avoid each fighter's identity hue, and fighters get their own rim light, so they pop everywhere.
- Each fighter has a home court: Rafa at Mosaic Promenade, Defne at The Cauldron, Rin at Ouendan Gym, Arjun at Floodlight Village.
- Each court has its own music and acoustics: taiko at Ouendan Gym, chenda at Floodlight Village, brass at Marigold Street, samba at Mosaic Promenade, boom-bap at The Overpass.
- Signage uses fictional brands only; no real teams, leagues or sponsors. Courts drawn from living traditions get a reviewer from that culture before final art.

## Modes and the CPU

Two players duel over Bluetooth, or one plays the CPU at five tiers from Rookie to Legend. Six modes ship at launch, and progression unlocks cosmetics only, never stats.

### Playing the computer

Each tier changes how fast the CPU reacts, how well it judges the ball, how cleanly it executes and how well it reads you. It plays by human rules: it sees only what is on screen, after a human-like reaction delay.

| Tier | Reaction | Landing read | Execution | Reads you | Learns your habits | Surge use |
| --- | --- | --- | --- | --- | --- | --- |
| Rookie | 30 frames (0.5 s) | ±1.2 m | Loose | 15% | No | Rarely spends |
| Club | 24 frames | ±0.8 m | Fair | 30% | No | Spends when full |
| Pro | 18 frames | ±0.5 m | Clean | 45% | Yes | Spends with purpose |
| National | 14 frames | ±0.3 m | Sharp | 60% | Yes | Saves for Denials |
| Legend | 12 frames (0.2 s) | ±0.15 m | Near-perfect | 70% | Yes | Optimal |

Landing read is the CPU's error in judging where the ball will land, shrinking as the ball falls. Reads you is the chance it picks the counter to your predicted choice. Habit learning tracks your attacks by set type, depth and score within a match, and Legend never reacts faster than 12 frames, around elite human speed.

- **Personalities:** Rafa jump-serves and hammers, Defne commits to blocks, Rin digs everything and counters, Arjun feints and dumps.
- **Rival mode:** adjusts the tier between sets to keep you near a 50% win rate.
- **Same pipe as players:** the CPU outputs controller inputs into the same sim, so replays and the Balance Lab work for free.

### Modes

- **Bluetooth Duel:** host or join a nearby court, iPhone or Android, best of 3 or 5 sets, instant rematch.
- **Quick Match:** any fighter, court and CPU tier.
- **World Tour:** a six-match ladder across all four themes, with illustrated story beats per fighter and a Legend-tier final at Skydome Finals.
- **Volley School:** interactive lessons on the Read, sets, feints, timing rings, Surge and Denial.
- **Training:** a serve machine, ring and frame-data display, hitbox view, and a dummy that records and replays your attacks.
- **Replays:** every match saved as inputs, a few kilobytes each; clip export comes later.

Fighter mastery levels unlock outfits, colors, ball skins, effect tints, intros and victory poses. Rep earned per match buys cosmetics, and an optional rewarded ad between matches doubles that match's Rep.

## Look and feel on a phone

The benchmark-grade look is reachable on recent phones because the camera never leaves the side view. Build only what the lens sees, light the fighters like heroes, and spend the savings on characters and effects. It is an art budget, not an engine setting, so a full-quality vertical slice decides go or no-go.

### What the reference shots teach

- **Rim light separates fighters from busy sets.** Every fighter carries a saturated edge light, so the eye finds them before the background.
- **Backgrounds are populated.** Spectators, musicians and bystanders react in every shot; the stage feels like an event, not a backdrop.
- **Hits are painted.** Impact effects read as brush and paint strokes in each fighter's color, layered over motion blur.
- **Each set has one idea.** Cherry blossoms and a shrine gate, a carrier deck, a ruined arena with chains, a lantern-lit market: one unmistakable picture per stage.
- **The HUD frames the fight.** Bold slanted bars and a large central counter leave the middle of the screen to the action.

These principles guide Volley Duel's own art and HUD; no layout, character or effect is copied.

### Camera and HUD

- **The dueling camera** frames both fighters and the ball, zooming from a 7 m-wide view for net battles to a 15 m-wide view for serves and deep digs. A 1.9 m fighter fills about 60% of the screen height at the closest framing and about 27% at the widest.
- **The ball never gets lost.** Its shadow always shows on the floor, and a ball above the frame shows an edge marker with its height.
- **Cinematic cuts are rare and earned:** Finishers, set point and match point.
- **An original HUD:** score bars that fill toward 7 at the top, set pips by the portraits, Stamina under each bar, a three-segment Surge gauge in the lower corners and the server marker center-top.

### Making it

- **Stylized realism.** Real materials on exaggerated anatomy (hands, shoulders and calves pushed 10–20%), hand-sculpted detail, and expressive faces with blendshapes for intros and wins.
- **Fighters lit like heroes.** Every court gets a per-fighter key, rim and fill light, separate from the baked stage lighting.
- **Courts built like film sets.** Three depth layers: the play plane in full detail, a low-poly baked mid-ground, and painted backdrop cards with parallax.
- **Crowds that cost little.** Hundreds of spectators as GPU-instanced meshes driven by vertex-animation textures baked in Blender, plus a few rigged hero spectators courtside.
- **Custom URP shaders.** Pre-integrated skin scattering, anisotropic hair, cloth sheen, sweat that builds over a match, sand and snow that take footprints, and floor reflections of the fighters only.
- **Effects with identity.** Rafa's vapor, Defne's tiles, Rin's ink and Arjun's mirage are shader-driven meshes and flipbooks, each with a GPU budget.
- **Animation with weight.** Strong key poses, anticipation, smear and hold frames. Mocap is only a base for footwork and locomotion, then pushed by hand.
- **Blender end to end.** Sculpt, retopologize, UV, rig, animate, bake crowd textures and export through validated scripts. Hero fighters are hand-built; AI image-to-3D is fine for background props after cleanup.
- **Concept art.** Explore looks with AI image tools by describing qualities (stylized realism, saturated rim light), never naming another game.

| Tier | Target | Rendering | Courts and effects |
| --- | --- | --- | --- |
| High (recent flagships) | 60 fps, near-native resolution | Full shaders, fighter reflections, 4× MSAA | Full crowds and effects |
| Mid (mainstream phones from the last \~4 years) | 60 fps, dynamic resolution | Lighter skin and hair, probe reflections | Thinner crowds, lighter effects |
| Low (floor devices) | 30–60 fps, adaptive | Baked lighting only, simple shaders | Card crowds, essential effects |

Starting budgets: 40–60k triangles per fighter at the top level of detail with 2K texture sets; p95 frame time of 16.6 ms or less on High and Mid; a base install of 200 MB or less with four courts, the rest downloaded on demand.

## Bluetooth cross-play

iPhone and Android play each other over Bluetooth Low Energy, the only Bluetooth link the two can share. Phones send only inputs; each runs the same deterministic game, and rollback netcode hides the radio's delay. The stack is identical to Dunk Duel's, so build it once as a shared Duel Core.

&#91;embedded content: netcode architecture · 2 phones, 1 BLE link\]

A late input never stalls the game: each phone predicts it, then rolls back up to 8 frames when it arrives.

- **Why our own BLE layer.** Classic Bluetooth data links on iPhone need Apple's MFi program, and Multipeer Connectivity is Apple-only. Google's Nearby Connections targets both, but developers in its tracker still report iOS–Android Bluetooth links not working ([GitHub](https://github.com/google/nearby/issues/1619)).
- **Roles.** The host advertises a court with a 4-character code; the guest scans and joins. The spike tests both role directions, and the pairing defaults to whichever measures better.
- **Packets.** 20 bytes: latest frame, acknowledged frame and the last 8 frames of input. That fits the default BLE packet size, and the overlap absorbs dropped packets.
- **Netcode.** A 60 Hz sim, 2 frames of input delay (adaptive 1–3) and up to 8 frames of rollback. State hashes cross every 30 frames to catch desyncs.
- **Determinism.** Fixed-point math, no floats in the sim and a shared seed, so an iPhone and an Android phone compute bit-identical frames, ball flight included.
- **Latency budget.** iOS accepts connection intervals from 15 ms in 15 ms steps, and some devices stretch a 15 ms request to 30 ms ([Apple](https://developer.apple.com/library/archive/qa/qa1931/_index.html)). Expect roughly 15–45 ms each way, which 2 frames of delay plus rollback can hide.
- **Wi-Fi Aware, a candidate upgrade.** iOS 26 opened Wi-Fi Aware to apps on iPhone 12 and later, for high-bandwidth, low-latency peer links ([Apple](https://developer.apple.com/documentation/wifiaware.md)). It is a managed capability that needs Apple's approval, and iOS requires six-digit PIN pairing ([Espressif, August 2026](https://developer.espressif.com/blog/2026/08/wifi-aware-esp-to-iphone/)). Phone-to-phone with Android is unproven: a September 2026 forum post calls it the biggest unknown ([Apple Developer Forums](https://developer.apple.com/forums/thread/845528)). Spike it behind the same interface and ship it only if it beats BLE.
- **Fallbacks.** Some Android phones can't advertise, so they can only join. An L2CAP channel and a same-Wi-Fi or hotspot transport sit behind the same interface.
- **Permissions.** iOS needs a Bluetooth usage description. Android 12 and later need scan, advertise and connect permissions; older Android needs location for scanning.
- **Lobby.** Host Court and Join Court, a nearby list with player names, instant rematch, a connection-quality bar, and a pause with a 20-second reconnect window on dropouts.

## Production plan and risks

Art, not code, is the critical path: plan on outside artists for four fighters and twelve courts, and gate everything on a full-quality vertical slice. Build the Duel Core once, and whichever of Dunk Duel or Volley Duel goes second skips most of its radio and netcode work.

&#91;embedded content: production roadmap · 6 phases, 6 gates, 1 art track\]

Durations are rough full-time weeks: about 24 in all, or about 21 if the Duel Core already exists. At a part-time pace of about 10 hours a week, expect roughly 4× the calendar time, which is why art must run in parallel from the start.

| Risk | Why it matters | Mitigation |
| --- | --- | --- |
| Visual bar vs. phone heat | Benchmark-grade fighters and courts can throttle mid-range phones | Vertical slice on a mid-tier phone in P0, device tiers, a 10-minute thermal soak gate |
| Reading the ball on a small screen | Volleyball lives on judging flight, and a fast ball on a 6-inch screen can vanish | The dueling camera, an always-on shadow, an edge marker, tunable ball speeds; playtests with new players in P1 |
| iOS–Android radio quirks | Platform GATT bugs, and phones that can't advertise | P0 radio spike in both role directions, an L2CAP option, a Wi-Fi fallback |
| Wi-Fi Aware unproven | Android interop with iOS 26 is undocumented and approval-gated | Treat it as optional, ship on BLE, request the entitlement early |
| Determinism bugs | One stray float breaks cross-play | Fixed-point sim, static checks, cross-device replay hash tests |
| Balance | Four kits interact in many ways | Data-driven tuning, the Balance Lab gate, human playtests |
| Art cost and scope | Four fighters and twelve courts at this fidelity is a large art budget | Launch with one court per theme and ship the other eight as free updates; share shells across variants; outsource the hero fighters |
| IP and naming | A close name or look-alike design invites takedowns | Original designs only. A 1v1 volleyball game on the App Store, [Volleyball Arena: All Star](https://apps.apple.com/us/app/volleyball-arena-all-star/id6474169201), still calls itself Volleyball Duel in its description. Run store and trademark searches before committing |
| Cultural representation | Real traditions drawn carelessly read as caricature | Reviewers from Kerala, Japan, Turkey, Brazil and Mexico check those fighters and courts before final art |

## Build prompt

Export this doc to Markdown and give the whole file to your coding agent, such as Claude Code. This section is the instruction; every section above it is the design spec. Diagrams drop out of the export, so everything the agent needs is also written here.

### Role and mission

You are the lead engineer for Volley Duel, a 1v1 volleyball fighting game for iOS and Android. Build it in Unity 6.3 LTS with URP; art is authored in Blender. Deliver deterministic 1v1 gameplay, four fighters, twelve courts, five CPU tiers, and iPhone–Android cross-play over Bluetooth Low Energy with rollback netcode.

### Contract: non-negotiables

1. Original IP only. No names, characters, UI layouts, sounds or system names from the benchmark game or its developer anywhere in code, assets or text.
2. The simulation is deterministic and engine-free: pure C#, 64-bit fixed-point math, a fixed 60 Hz tick. No UnityEngine, float, double, System.Random, DateTime or order-dependent dictionary iteration.
3. Simulation and presentation are separate. Presentation reads sim state and sim events; the sim never reads presentation.
4. Every gameplay number lives in data assets, baked into immutable sim tables. No magic numbers in code.
5. Local multiplayer needs no server: BLE behind an `ITransport` interface, with LAN UDP as the second implementation and Wi-Fi Aware as an optional third.
6. Reuse before rebuilding. If Dunk Duel's Duel Core package exists, depend on it. If not, build the sim framework, netcode, transports, CPU framework and Balance Lab as a separate package, `com.duel.core`, with no volleyball code in it.
7. Landscape only, safe areas respected, 60 fps on High and Mid tiers.
8. Ask before adding any paid asset, SDK or service, and before requesting any platform entitlement.

### Execution protocol

- **Inner loop, every change:** compile in batch mode, run analyzers and the format check, run EditMode tests. Never move on from a red loop.
- **Outer loop, every phase:** a security pass (permissions, Bluetooth and Wi-Fi Aware pairing, no personal data in logs), a performance pass (device captures against tier budgets) and a data-integrity pass (determinism hashes, replay round-trips, save-file integrity).
- **Loop only where a gate exists.** Iterate on your own wherever an automated check can judge the result. Anything judged by eye (feel, ball readability, visuals, animation) ends at a human review checkpoint with a build and a short screen recording.
- **CLAUDE.md first.** Create it at the repo root with these rules, the folder layout, build and test commands, and the gate list. Keep it current.
- **Write decisions down.** DECISIONS.md records each decision, the options considered and the reason.
- **Use the Editor bridge when connected.** If the Unity MCP server is available, use it to read the console, run tests and inspect scenes. Never hand-edit scene or prefab YAML when an Editor API exists.

### Architecture

| Assembly | Owns | Depends on |
| --- | --- | --- |
| Duel.Core.Sim | Fixed-point math, RNG streams, state hashing, input buffer, frame loop | Nothing |
| Duel.Core.Net | Rollback session, time sync, desync recovery, `ITransport` | Core.Sim |
| Duel.Core.Transport.BLE | CoreBluetooth (Swift) and Android BLE (Kotlin) bridges | Core.Net |
| Duel.Core.Transport.LAN | UDP fallback over shared Wi-Fi or a hotspot | Core.Net |
| Duel.Core.Transport.WiFiAware | Optional Wi-Fi Aware bridges for iOS and Android | Core.Net |
| Duel.Core.AI | Tier framework: reaction delay, perception error, habit model, utility scoring | Core.Sim |
| VolleyDuel.Sim | Rules, ball flight, touches, the Read, meters, fighters | Core.Sim |
| VolleyDuel.AI | Volleyball CPU decisions, emitted as inputs | Core.AI, VolleyDuel.Sim |
| VolleyDuel.Presentation | Frame-locked animation, dueling camera, VFX, audio, haptics | VolleyDuel.Sim |
| VolleyDuel.UI | Menus, HUD, lobby | Presentation, Core.Net |
| Tools | Blender scripts, import pipeline, Balance Lab runner | Sim and AI assemblies |

### Contracts to define before implementation

- `GameState`: a value-type snapshot of at most 2 KB with `Clone()` and `Hash()`. `FrameInput`: 16 bits. `SimEvent`: frame, type, actor, sequence.
- `ISimulation.Step(in GameState, FrameInput p1, FrameInput p2)` returns the next state plus that frame's events.
- `ITransport`: `Host(code)`, `Join(peer)`, `Send(bytes)`, `OnReceive`, `OnStateChanged`, and `Stats` (RTT, jitter, loss).
- Data schemas: `FighterDefinition`, `MoveDefinition` (start-up, active, recovery, cancel windows, flags such as BaitWindow, LateLock and Unblockable), `CourtDefinition`, `BallTuning`, `Tuning`.
- The BLE protocol and the session handshake below.

### Simulation spec

- **Court** in meters: X runs from −6 (player 1's end line) to +6 (player 2's end line), the net sits at X = 0 with its top at 2.35 m, and Y is up. Each service zone extends 1 m behind its end line. The ball's radius is 0.105 m.
- **Ball:** gravity 9.81 m/s², integrated at 60 Hz in fixed point. No drag, except a data-tuned wobble on float serves and a dip on topspin jump serves, both drawn from the match RNG at contact.
- **Speeds:** spikes start at 17 m/s plus 1 m/s per Power point above 5, and a full charge, held for 12 frames, adds 15%. Tips travel at 4–6 m/s. All speeds are data, tuned for readability in P1 playtests.
- **Touches:** three per side, and a block touch is free. A fourth touch, a net touch, a foot across the center line or a ball landing out ends the rally; a ball touching the line is in.
- **Contact rings:** every touch opens a ring timed to the ball's arrival. Perfect is 4 frames, plus or minus 1 per 2 attribute points away from 5; Good is 10 frames, plus or minus 1 per point. Rin's Nothing Drops adds 2 Perfect frames to her digs.
- **Blocks:** the block ring centers on the hitter's contact frame. Perfect (±2 frames) stuffs, Good (±5) softens into a free ball on the blocker's side, and later contact is tooled out for a point to the hitter. Defne's Good blocks stuff, and her block hitbox is 20% wider.
- **Tips:** a tip over a blocker who can't land in time scores; a tip into a grounded defender becomes a free ball and gives the defender 20 Surge.
- **Feints:** a defender commitment that starts inside a feint's bait window bites: −15 Stamina and 8 extra frames of landing recovery.
- **Sets:** a quick set peaks 0.6 m above the net, cuts the blocker's read by 8 frames and scales spike power by 0.85. A high set peaks 3 m above the net at full power and widens the block ring by 2 frames. A missed set ring allows only a weak hit (power ×0.6) or a tip.
- **Surge** runs 0–300. Starting gains: kill +30, ace +40, stuff block +50, Perfect dig +25, floor save +35, read tip +20, joust win +30, bite drawn +10, scored on +10. Costs: Amped Trick 100, Denial 100, Finisher 300.
- **Stamina** runs 0–100. Costs: jump 12, dive 18, jump serve 20, bite 15. It refills 20 per second while grounded and idle, plus 40 at each dead ball, and resets each set. At 0 a fighter is Gassed (jump height −25%, dive range −30%) until it refills to 30.
- **On a Run:** after three straight points, the next attack's Perfect window grows 50%; losing that rally gives the opponent 100 Surge.
- **Starting frame data:** offensive Tricks 8–16 frames start-up and 14–28 recovery; defensive Tricks 6–12 start-up and 20–32 whiff recovery; Amped versions −3 frames start-up plus one added property; Finishers 120–180 frames with one 4–6-frame Denial window.
- **Scoring:** rally scoring to 7, win by 2, capped at 9; a Finisher scores 2; best of 3 sets.
- **Input:** a 6-frame buffer; a direction and a button within 3 frames count as simultaneous.

**Contact model,** computed at every touch:

```latex
Q = T + 0.03\,(A - 5) - P - S
```

T is timing: Perfect 1.00, Good 0.65, Late 0.30. A is the touch's attribute: Dig for receives, Control for sets and tips, Power for spikes and serves. P is incoming pressure, up to 0.35 from ball speed, plus 0.15 from Heavy Ball. S is up to 0.15 below 30 Stamina.

Q of 0.85 or more is controlled, 0.55–0.85 is loose with growing aim error, 0.30–0.55 is a shank whose direction comes from the match RNG, and below 0.30 is a miss.

### Netcode spec

- **Handshake:** protocol version; sim version, a build-time hash of sim code and data tables that refuses mismatched builds; fighters; court; seed = host seed XOR guest seed; RTT samples.
- **Rollback:** 2 frames of input delay (adaptive 1–3), prediction by repeating the last input, at most 8 frames of rollback; beyond that, the leading peer slows one frame at a time.
- **State:** a 16-frame snapshot ring buffer. Resimulating 8 frames must take under 1 ms on a Low-tier phone.
- **Desyncs:** compare hashes every 30 frames. On a mismatch the host sends full state in chunks, and dev builds dump both states for diffing.
- **Disconnects:** 3 seconds of silence pauses the match; players get 20 seconds to reconnect, otherwise no contest.
- **Rollback-aware presentation:** an event ledger keyed by frame, type, actor and sequence. After a resimulation, fade orphaned effects out fast, spawn missing ones with a time offset, and blend the ball's trail onto its corrected flight over 4 frames.

### BLE transport spec

- One custom GATT service with two characteristics: INPUT (write without response, plus notify) and CONTROL (write with response, plus indicate).
- **Input packet, 20 bytes:** u16 latest frame, u16 acknowledged frame, 8 × u16 inputs.
- **Roles:** the host is the peripheral, advertising the service UUID and a local name carrying the 4-character court code; the guest is the central, scanning by service UUID. Measure both role directions in P0 and default mixed pairs to the better one.
- **iOS (Swift, CoreBluetooth):** request low connection latency, honor write-without-response flow control, and include NSBluetoothAlwaysUsageDescription.
- **Android (Kotlin):** advertiser plus GATT server to host, scanner plus GATT client to join, high connection priority. Request scan, advertise and connect permissions on Android 12+ and location on older versions; a phone that can't advertise can only join.
- **Unity bridge:** DllImport on iOS, AndroidJavaObject on Android, a lock-free queue, and callbacks marshalled to the main thread.
- **L2CAP option:** an L2CAP channel implementation behind the same interface, kept if it measures better in the spike.
- **Telemetry:** a debug overlay and session log with RTT, jitter, loss, a rollback histogram and the desync count.

### Wi-Fi Aware spike (optional)

- **iOS:** the Wi-Fi Aware and Network frameworks, pairing through DeviceDiscoveryUI, services declared in Info.plist, and the `com.apple.developer.wifi-aware` entitlement. Ask before requesting it.
- **Android:** WifiAwareManager publish and subscribe with the same DNS-SD service name. Confirm the device supports the PIN pairing iOS requires.
- **Gate:** an iPhone–Android pair discovers, pairs and holds a 60 Hz echo for 10 minutes with lower RTT and jitter than BLE. If it fails, the transport stays off.

### CPU spec

- The AI emits `FrameInput`s from a state snapshot delayed by its tier's reaction frames, using its own RNG stream.
- **Perception:** it estimates the landing point from the visible ball with its tier's landing-read error, shrinking to zero as the ball falls.
- **Utility scoring** covers serve choice, receive, set type, attack (spike, tip, roll, Trick, Finisher) and defense (block, dig, fake block, dive), with per-fighter weights.
- Tier parameters come from the CPU section. The habit model keeps rolling counts of the player's attacks per set type, depth and score bucket.
- **No cheating:** never react to an input before its animation is visible, and never see RNG outcomes early.
- Rival mode nudges tier parameters between sets toward a 50% win rate.

### Presentation spec

- **Frame-locked animation:** each sim state maps to a clip and frame, sampled by sim frame through Playables. Animator transitions never drive logic, and root motion is baked from Blender into fixed-point curves at import.
- **Dueling camera:** frames both fighters and the ball, with the view width clamped to 7–15 m and damped toward its target; the net never leaves the frame. Cinemachine and Timeline handle intros, Finishers and wins, and each cinematic lasts exactly as long as its sim state.
- **Ball readability:** an always-on ground shadow, an off-screen edge marker with height, a speed-scaled trail, and an optional landing ring in Simple mode.
- **Impact recipes:** hit-stop, camera move, time scale and callout as data, per the feel table.
- **HUD:** score bars filling toward 7, set pips, Stamina bars, 3-segment Surge, a server marker, contact rings and callouts, in uGUI with TextMeshPro.
- **Feedback:** haptics through a thin Core Haptics and Android vibration plugin, layered music stems driven by Stage Heat, and per-court reverb.
- **Quality tiers:** High, Mid and Low presets with dynamic resolution and thermal monitoring.

### Content pipeline

- **Blender conventions:** meters, applied transforms, names `CH_<Fighter>_<Part>` and `ST_<Court>_<Prop>`, LOD suffixes `_LOD0` to `_LOD2`, and texture sets of BaseColor, Normal and ORM (plus Emission) at 2K maximum.
- **Scripts in Tools/Blender:** batch export with validation (scale, transforms, triangle budgets, bone names), a vertex-animation-texture baker for crowds, and LOD generation for props.
- **Unity import:** an AssetPostprocessor applies import settings, materials and Addressables groups by folder, with a validation report window.
- **Graybox first:** mannequins at the roster heights and graybox courts at the Duel Court dimensions, so gameplay is complete before final art lands. Swapping art must never change gameplay.

### Gates

- **Determinism:** a 10,000-frame recorded replay, ball flight included, hashes identically in EditMode, on an iPhone and on an Android phone.
- **Static:** tests fail if any Sim assembly references UnityEngine, float, double, System.Random or DateTime.
- **Rules:** unit tests for every touch, fault, scoring rule, Read outcome, the contact model and the meters.
- **Balance Lab:** a headless .NET runner; from P3 on, every pairing stays within 45–55%, and the receiver wins 55–60% of rallies.
- **Performance:** p95 frame time of 16.6 ms or less on reference High and Mid phones, a 10-minute thermal soak holding 55 fps or more, and memory under each tier's ceiling.
- **Netcode soak:** 30 iPhone–Android matches over BLE with zero desyncs.

### Phases

| Phase | Delivers | Gate |
| --- | --- | --- |
| P0 Spikes, 3 weeks (2 with Duel Core) | BLE echo between iPhone and Android at 60 Hz for 10 minutes in both role directions; a Wi-Fi Aware echo attempt; a cross-device determinism test with ball flight; a look-dev slice of one fighter on one court on a mid-tier phone | RTT, jitter and slice frame time recorded in SPIKES.md; a go/no-go decision |
| P1 Graybox core, 4 weeks | Sim, rules, ball flight, touches, the Read, meters, a graybox mirror match, a basic CPU, Training | A full match on device; all rule tests green; a ball-readability review |
| P2 Netcode, 3 weeks (1 with Duel Core) | Rollback, BLE and LAN transports, lobby, rematch, reconnect | 30-match soak with zero desyncs; rollback p95 of 4 frames or less |
| P3 Roster and AI, 4 weeks | Four kits in graybox, five CPU tiers, Rival mode, the Balance Lab | Every pairing within 45–55%; CPU win rates rise with each tier |
| P4 Content, 6 weeks | Final fighters and courts as they land, VFX, audio, HUD, quality tiers, Addressables | Performance gates on reference phones; base install of 200 MB or less |
| P5 Modes and polish, 4 weeks | World Tour, Volley School, settings, remapping, accessibility, localization-ready text | A feature-complete release candidate |

### Reporting

End every phase with what shipped, gate results with numbers, known issues and the next step. When the spec is ambiguous, propose two options with a recommendation and wait. Out of scope for this build: online play, accounts, payments, ads and cloud saves; keep their interfaces ready, the rewarded-ad hook included.

## Sources

- Dunk Duel — Game Concept & Build Prompt, the sibling concept this doc mirrors
- [Unity 6 release support](https://unity.com/releases/unity-6/support), Unity
- [Changes to Unity subscription plans and pricing](https://unity.com/products/pricing-updates), Unity
- [Unity's AI tools in beta: How to get started with MCP](https://unity.com/blog/unity-ai-mcp-how-to-get-started), Unity, May 11, 2026
- [Lumen on Mobile](https://dev.epicgames.com/documentation/unreal-engine/using-lumen-global-illumination-on-mobile-in-unreal-engine?lang=en-US), Epic Games, Unreal Engine 5.8 documentation
- [Mali and Unreal Engine's Nanite: Enabling the future of mobile graphics](https://developer.arm.com/community/arm-community-blogs/b/mobile-graphics-and-gaming-blog/posts/mali-and-unreal-engine-s-nanite-enabling-the-future-of-mobile-graphics), Arm, March 18, 2026
- [Sports dimensions guide: volleyball](https://dlgsc.wa.gov.au/sport-and-recreation/sports-dimensions-guide/volleyball), Government of Western Australia
- [Nearby Connections issue 1619](https://github.com/google/nearby/issues/1619), GitHub
- [Technical Q&A QA1931: Bluetooth LE advertising and connection parameters](https://developer.apple.com/library/archive/qa/qa1931/_index.html), Apple
- [Wi-Fi Aware framework](https://developer.apple.com/documentation/wifiaware.md), Apple
- [Connect ESP with an iPhone directly using Wi-Fi Aware](https://developer.espressif.com/blog/2026/08/wifi-aware-esp-to-iphone/), Espressif, August 25, 2026
- [Looking for collaborators to test Wi-Fi Aware on iOS 26 and Android interoperability](https://developer.apple.com/forums/thread/845528), Apple Developer Forums, September 2026
- [Volleyball Arena: All Star](https://apps.apple.com/us/app/volleyball-arena-all-star/id6474169201), App Store
