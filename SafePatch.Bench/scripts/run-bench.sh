#!/usr/bin/env bash
# Parity, hash and thread checks for every variant built by build-matrix.sh, then the timings.
source "$(dirname "$0")/env.sh"
VARIANTS="base fix-unsigned-enums fix-nullable-fallback fix-overlay-defaults fix-cumulative-breaks fix-gendered-equality fix-condition-pack-data fix-content-equality fix-overlay-float-epsilon perf-group-cache perf-deferred-fill perf-deferred-fill-peek perf-deferred-fill-placed perf-record-batches combined"
for v in $VARIANTS; do
  echo "== parity $v $(date +%T)"
  dotnet "$OUT/$v/SafePatch.Bench.dll" check "$D" $M "$v"
  dotnet "$OUT/$v/SafePatch.Bench.dll" check-hash "$D" $M "$v"
done
for v in perf-deferred-fill perf-deferred-fill-placed combined; do
  echo "== threads $v $(date +%T)"
  dotnet "$OUT/$v/SafePatch.Bench.dll" check-threads "$D" Dawnguard.esm
done
"$SCRIPTS/run-timing.sh"
