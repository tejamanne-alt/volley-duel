# Replay viewer

Draws a Balance Lab trace in the browser: the side view, fighters posed by sim action, the ball
with its shadow and trail, timing rings, the HUD, callouts, frame data and an event log. It is for
reviewing sim output by eye before Unity graybox builds exist. Everything shown is the
deterministic sim's own state.

```bash
dotnet run -c Release --project Headless/VolleyDuel.BalanceLab -- trace --p0 rafa --p1 rin --tier legend --seed 3 --frames 5400 --out Tools/ReplayViewer/trace.json
```

```bash
cd Tools/ReplayViewer && python3 -m http.server 8765
```

Open http://127.0.0.1:8765/viewer.html. Keys: Space plays and pauses, comma and period step one
frame, `[` and `]` change speed. Use the "Open trace…" button for any other trace file.

`viewer.html` has no `<html>` or `<head>` wrapper because it is also published as a claude.ai
page. Browsers render it as is.
