#!/usr/bin/env bash
# Correctness checks for the combined build: parity, hash consistency and concurrent reads, written to $LOGS.
source "$(dirname "$0")/env.sh"
ALL=$(all_plugins)
B="$OUT/combined/SafePatch.Bench.dll"
dotnet "$B" check "$D" $M combined > "$LOGS/parity-combined3.log" 2>&1
dotnet "$B" check "$D" "$ALL" combined-all > "$LOGS/parity-all-combined.log" 2>&1
dotnet "$B" check-hash "$D" $M combined > "$LOGS/hash-combined.log" 2>&1
dotnet "$B" check-hash "$D" "$ALL" combined-all > "$LOGS/hash-all-combined.log" 2>&1
{ echo "== threads combined"; dotnet "$B" check-threads "$D" Dawnguard.esm; } > "$LOGS/threads-combined.log" 2>&1
cd "$LOGS" && grep -h "differing from\|Unequal to\|hash differs\|concurrent" parity-combined3.log parity-all-combined.log hash-combined.log hash-all-combined.log threads-combined.log | sed -E 's/ in [^;]*;/;/' | cut -c1-200
