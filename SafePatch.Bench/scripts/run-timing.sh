#!/usr/bin/env bash
# Timings only, round-robin: each round runs every variant once, so a busy spell on the machine is spread across
# variants instead of landing on one.
set -u
OUT=/c/Users/matth/source/repos/Mutagen-wt/bench-out
D="D:/SteamLibrary/steamapps/common/Skyrim Special Edition/Data"
P="C:/Users/matth/AppData/Local/Temp/claude/c--Users-matth-source-repos-SynthesisMCP/488b0f26-3ebe-40c5-86ae-97a8c84a2d6c/scratchpad/bench/vanilla-plugins.txt"
VARIANTS="base fix-unsigned-enums fix-nullable-fallback fix-overlay-defaults fix-cumulative-breaks fix-gendered-equality fix-condition-pack-data fix-byte-list-equality perf-group-cache perf-deferred-fill perf-deferred-fill-peek perf-deferred-fill-placed perf-record-batches combined"
for round in 1 2 3; do
  for v in $VARIANTS; do
    echo "== round $round $v $(date +%T)"
    dotnet "$OUT/$v/SafePatch.Bench.dll" "$D" "$P" 3 - "$v"
  done
done
echo "done $(date +%T)"
