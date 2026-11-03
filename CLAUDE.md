# Volley Duel — agent guide

1v1 volleyball fighting game for iOS and Android. Unity 6.3 LTS + URP; art in Blender.
The design spec is `docs/Volley_Duel_Concept.md` (the concept and build prompt). Decisions
and spec interpretations are in `DECISIONS.md`; measured spike results go in `SPIKES.md`.

## Contract (non-negotiable)

1. **Original IP only.** No names, characters, UI layouts, sounds or system names from any
   existing game or franchise, in code, assets or text. The concept doc names one fighting
   game as a quality benchmark; that name never appears in this repo.
2. **Deterministic, engine-free sim.** Pure C#, Q32.32 fixed point (`Fix64`), fixed 60 Hz
   tick. No `UnityEngine`, `float`, `double`, `decimal`, `System.Random`, `DateTime`,
   clocks, threads, `Dictionary`/`HashSet` (order-dependent iteration). Enforced by the
   static gate (`Headless/Gates/StaticDeterminismGate.cs`) on every Sim and AI assembly.
3. **Sim and presentation are separate.** Presentation reads sim state and `SimEvent`s;
   the sim never reads presentation. Hidden information goes through
   `VolleyDuel.Sim.Visibility` for both the CPU and the screen.
4. **Every gameplay number lives in data** (`Assets/VolleyDuel/Resources/Data/*.json`),
   baked into immutable sim tables. No magic numbers in sim code.
5. **No server for local multiplayer.** BLE behind `ITransport`, LAN UDP second, Wi-Fi Aware
   optional third (off; needs an entitlement — ask first).
6. **Duel Core is game-agnostic.** `Packages/com.duel.core` holds the sim framework,
   netcode, transports, CPU framework and Balance Lab plumbing, with no volleyball code.
   Dunk Duel should depend on it.
7. Landscape only, safe areas respected, 60 fps on High and Mid tiers.
8. **Ask before** adding any paid asset, SDK or service, or requesting a platform entitlement.

## Layout

```
Assets/VolleyDuel/
  Sim/            VolleyDuel.Sim           rules, ball flight, touches, the Read, meters, fighters
  AI/             VolleyDuel.AI            CPU decisions, emitted as FrameInputs
  Presentation/   VolleyDuel.Presentation  graybox views, dueling camera, impact recipes, VFX/audio hooks
  UI/             VolleyDuel.UI            HUD, touch controls, menus, lobby
  Editor/         VolleyDuel.Editor        project setup, scene builder, data validation, sim hash
  Tests/EditMode/ VolleyDuel.Tests         rules + determinism tests (also run headless)
  Resources/Data/ tuning.json, fighters/*.json, courts/*.json, manifest.json
Packages/com.duel.core/
  Runtime/Sim     Duel.Core.Sim            Fix64, Rng streams, StateWriter/hash, FrameInput, InputTracker, events, replay, JSON
  Runtime/Net     Duel.Core.Net            rollback session, handshake, time sync, desync recovery, ITransport, event ledger
  Runtime/AI      Duel.Core.AI             reaction delay, perception error, habit model, utility scoring
  Runtime/Transport.LAN       Duel.Core.Transport.LAN        UDP, reliable control channel, beacon discovery
  Runtime/Transport.BLE       Duel.Core.Transport.BLE        BleTransport + IBleBridge (iOS DllImport, Android JNI)
  Runtime/Transport.WiFiAware Duel.Core.Transport.WiFiAware  placeholder, off (entitlement not requested)
  Plugins/iOS/DuelBle.swift, Plugins/Android/DuelBle.kt      native BLE bridges (need the P0 device spike)
  Editor          Duel.Core.Editor         build postprocess: Android permissions, iOS plist/Swift settings
  Tests/EditMode  Duel.Core.Tests
Headless/          .NET 8 solution compiling the engine-free assemblies (C# 9, netstandard2.1, like Unity)
  Gates/           headless-only gates: static determinism scan, sim version hash
  UnityApiCheck/   compiles Presentation, UI, Editor and the BLE bridges against Unity API stubs
                   (4 variants: editor/player x Android/iOS); a type check, not a Unity build
Tools/BalanceLab   headless CPU-vs-CPU runner (csproj in Headless/)
Tools/ReplayViewer replay/trace viewer for reviewing sim output by eye
Tools/Blender      export validation, VAT crowd baker, LOD scripts (P4)
scripts/           check.sh (inner loop), sim_hash.py, gen_meta.py
```

Unity never sees `Headless/`, `Tools/`, `scripts/` or `docs/`. Unity regenerates root-level
`*.csproj`/`*.sln`; those are gitignored. Headless projects live only in `Headless/`.

## Commands

```bash
./scripts/check.sh                       # inner loop: sim hash, build, format, analyzers, all tests
dotnet test Headless/VolleyDuel.Headless.sln            # tests only
dotnet run -c Release --project Headless/VolleyDuel.BalanceLab -- --help
dotnet format whitespace Headless/VolleyDuel.Headless.sln # fix formatting
python3 scripts/gen_meta.py              # add missing Unity .meta files (deterministic GUIDs)
dotnet run -c Release --project Headless/VolleyDuel.BalanceLab -- netsoak --matches 30   # netcode soak gate
```

In Unity: `Volley Duel/Setup Project`, then `Volley Duel/Build Scenes`, then press Play in
`Assets/VolleyDuel/Scenes/Main.unity`. Keyboard: A/D move, W/S up/down, J HIT, K JUMP, L FEINT,
U TRICK, I Amped TRICK, O FINISHER/DENY.

The .NET SDK in the cloud container comes from Ubuntu's `dotnet-sdk-8.0` package.

## Language level

Everything compiled by Unity targets **C# 9 / .NET Standard 2.1**: block-scoped namespaces,
no records, no `init`, no file-scoped namespaces, no global usings, no generic math, no raw
string literals. The headless build enforces this (`LangVersion 9.0`, `netstandard2.1`).
Tests use NUnit 3 (same as the Unity Test Framework) so EditMode tests run in both places.

## Gates

| Gate | Where | Status |
| --- | --- | --- |
| Static: no UnityEngine/float/double/Random/DateTime in Sim + AI | `Headless/Gates` | automated |
| Rules: every touch, fault, scoring rule, Read outcome, contact model, meters | `Assets/VolleyDuel/Tests/EditMode` | automated |
| Determinism: 10,000-frame golden replay hash (EditMode; device runs pending) | tests + `Tools/BalanceLab golden` | automated here; devices need a human |
| Sim version hash matches sim sources | `Headless/Gates` | automated |
| Balance Lab: pairings 45–55%, receiver wins 55–60% of rallies, tiers monotonic | `Tools/BalanceLab gate` | automated |
| Netcode: rollback soak over a simulated lossy link, zero desyncs | `Packages/com.duel.core/Tests`, `NetplayTests`, `Tools/BalanceLab netsoak` | automated; 30-match device BLE soak needs phones |
| Performance, thermal, BLE RTT, look-dev slice | devices | human |

Anything judged by eye (feel, ball readability, visuals, animation) ends at a human review
checkpoint with a build and a short screen recording. Loop on your own only where an
automated check can judge the result.

## Working rules

- Keep sim state a value type (`GameState`, ≤ 2 KB serialized). Every field must be written
  by `Write`/`Read`; the field-coverage test mutates each field and expects the hash to change.
- New sim numbers go into the JSON data and the table baker, never into code.
- Events are the only sim → presentation channel besides reading state.
- Never hand-edit scene or prefab YAML; scenes are built by `VolleyDuel.Editor` menu items.
- If the Unity MCP server is connected, use it to read the console, run tests and inspect scenes.
- Record decisions (options considered, reason) in `DECISIONS.md`; keep this file current.
