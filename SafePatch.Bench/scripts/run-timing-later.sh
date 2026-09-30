#!/usr/bin/env bash
# Round-robin timings for the three later fix branches and the rebuilt combined build, with 0.54.4 alongside.
set -u
OUT=/c/Users/matth/source/repos/Mutagen-wt/bench-out
D="D:/SteamLibrary/steamapps/common/Skyrim Special Edition/Data"
P="C:/Users/matth/AppData/Local/Temp/claude/c--Users-matth-source-repos-SynthesisMCP/488b0f26-3ebe-40c5-86ae-97a8c84a2d6c/scratchpad/bench/vanilla-plugins.txt"
for round in 1 2 3; do
  for pair in "base base-2" "fix-condition-pack-data fix-condition-pack-data" "fix-content-equality fix-content-equality" "fix-overlay-float-epsilon fix-overlay-float-epsilon" "combined combined-2"; do
    set -- $pair
    echo "== round $round $2 $(date +%T)"
    dotnet "$OUT/$1/SafePatch.Bench.dll" "$D" "$P" 3 - "$2"
  done
done
echo "done $(date +%T)"
