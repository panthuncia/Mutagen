#!/usr/bin/env bash
# Round-robin timings for the three later fix branches and the rebuilt combined build, with 0.54.4 alongside
# (labelled base-2 and combined-2). Output: $LOGS/timing-new.log.
source "$(dirname "$0")/env.sh"
for round in 1 2 3; do
  for pair in "base base-2" "fix-condition-pack-data fix-condition-pack-data" "fix-content-equality fix-content-equality" "fix-overlay-float-epsilon fix-overlay-float-epsilon" "combined combined-2"; do
    set -- $pair
    echo "== round $round $2 $(date +%T)"
    dotnet "$OUT/$1/SafePatch.Bench.dll" "$D" "$P" 3 - "$2"
  done
done > "$LOGS/timing-new.log" 2>&1
echo "done $(date +%T)" >> "$LOGS/timing-new.log"
