#!/usr/bin/env bash
set -u
OUT=/c/Users/matth/source/repos/Mutagen-wt/bench-out
D="D:/SteamLibrary/steamapps/common/Skyrim Special Edition/Data"
P="C:/Users/matth/AppData/Local/Temp/claude/c--Users-matth-source-repos-SynthesisMCP/488b0f26-3ebe-40c5-86ae-97a8c84a2d6c/scratchpad/bench/vanilla-plugins.txt"
VARIANTS="base fix-unsigned-enums fix-nullable-fallback fix-overlay-defaults fix-cumulative-breaks fix-gendered-equality fix-condition-pack-data fix-content-equality perf-group-cache perf-deferred-fill perf-deferred-fill-peek perf-deferred-fill-placed perf-record-batches combined"
for v in $VARIANTS; do
  echo "== parity $v $(date +%T)"
  dotnet "$OUT/$v/SafePatch.Bench.dll" check "$D" Skyrim.esm,Update.esm,Dawnguard.esm,HearthFires.esm,Dragonborn.esm "$v"
  dotnet "$OUT/$v/SafePatch.Bench.dll" check-hash "$D" Skyrim.esm,Update.esm,Dawnguard.esm,HearthFires.esm,Dragonborn.esm "$v"
done
for v in perf-deferred-fill perf-deferred-fill-placed combined; do
  echo "== threads $v $(date +%T)"
  dotnet "$OUT/$v/SafePatch.Bench.dll" check-threads "$D" Dawnguard.esm
done
/c/Users/matth/source/repos/Mutagen-wt/run-timing.sh
