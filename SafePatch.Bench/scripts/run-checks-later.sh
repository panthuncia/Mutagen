#!/usr/bin/env bash
# Parity and hash checks for the later fix branches against 0.54.4, written to $LOGS.
source "$(dirname "$0")/env.sh"
ALL=$(all_plugins)
dotnet "$OUT/fix-condition-pack-data/SafePatch.Bench.dll" check "$D" $M fix-condition-pack-data > "$LOGS/parity-new.log" 2>&1
dotnet "$OUT/fix-content-equality/SafePatch.Bench.dll" check "$D" $M fix-content-equality > "$LOGS/parity-content.log" 2>&1
dotnet "$OUT/fix-overlay-float-epsilon/SafePatch.Bench.dll" check "$D" $M fix-overlay-float-epsilon > "$LOGS/parity-epsilon.log" 2>&1
dotnet "$OUT/base/SafePatch.Bench.dll" check "$D" "$ALL" base-all > "$LOGS/parity-all-base.log" 2>&1
for v in base fix-content-equality fix-overlay-float-epsilon; do dotnet "$OUT/$v/SafePatch.Bench.dll" check-hash "$D" $M $v; done > "$LOGS/hash-masters.log" 2>&1
dotnet "$OUT/base/SafePatch.Bench.dll" check-hash "$D" "$ALL" base-all > "$LOGS/hash-all.log" 2>&1
cd "$LOGS" && grep -h "differing from\|Unequal to\|hash differs" parity-new.log parity-content.log parity-epsilon.log parity-all-base.log hash-masters.log hash-all.log | sed -E 's/ in [^;]*;/;/' | cut -c1-200
