#!/usr/bin/env bash
# Timings only, round-robin: each round runs every variant once, so a busy spell on the machine is spread across
# variants instead of landing on one.
source "$(dirname "$0")/env.sh"
VARIANTS="base fix-unsigned-enums fix-nullable-fallback fix-overlay-defaults fix-cumulative-breaks fix-gendered-equality fix-condition-pack-data fix-content-equality fix-overlay-float-epsilon perf-group-cache perf-deferred-fill perf-deferred-fill-peek perf-deferred-fill-placed perf-record-batches combined"
for round in 1 2 3; do
  for v in $VARIANTS; do
    echo "== round $round $v $(date +%T)"
    dotnet "$OUT/$v/SafePatch.Bench.dll" "$D" "$P" 3 - "$v"
  done
done
echo "done $(date +%T)"
