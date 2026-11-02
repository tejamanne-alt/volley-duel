#!/usr/bin/env bash
# Inner loop for every change: regenerate the sim version hash, compile, check formatting,
# run analyzers (warnings are errors) and all headless tests. Never move on from a red loop.
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

if [ -f scripts/sim_hash.py ]; then
  python3 scripts/sim_hash.py --write
fi

echo "== build (analyzers on, warnings are errors)"
dotnet build Headless/VolleyDuel.Headless.sln -c Release -nologo -v quiet

echo "== format check"
dotnet format whitespace Headless/VolleyDuel.Headless.sln --verify-no-changes -v quiet

echo "== tests"
dotnet test Headless/VolleyDuel.Headless.sln -c Release --no-build -nologo -v quiet

if [ -f scripts/gen_meta.py ]; then
  echo "== unity .meta files"
  python3 scripts/gen_meta.py --check
fi
echo "== green"
