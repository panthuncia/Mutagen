#!/usr/bin/env bash
# Builds SafePatch.Bench against each variant into bench-out/<name>.
set -u
cd /c/Users/matth/source/repos/Mutagen-wt/pr
while read -r name ref flags; do
  [ -z "$name" ] && continue
  echo "== $name ($ref) $(date +%T)"
  git checkout -q --detach "$ref" || { echo "checkout failed"; continue; }
  rm -rf SafePatch.Bench/Program.cs SafePatch.Bench/SafePatch.Bench.csproj
  mkdir -p SafePatch.Bench && cp ../bench-src/SafePatch.Bench/* SafePatch.Bench/
  rm -rf "../bench-out/$name"
  dotnet build SafePatch.Bench -c Release -o "../bench-out/$name" -p:DisableGitVersionTask=true -p:GeneratePackageOnBuild=false \
    -p:SafePatchRepo=C:/Users/matth/source/repos/SynthesisMCP $flags 2>&1 | grep -E " error |rror\(s\)" | sort -u | head -5
done <<'LIST'
base 0188012
fix-unsigned-enums fix/unsigned-two-byte-enums
fix-nullable-fallback fix/nullable-enum-fallback-parse
fix-overlay-defaults fix/overlay-declared-defaults
fix-cumulative-breaks fix/cumulative-break-flags
fix-gendered-equality fix/gendered-item-equality
fix-condition-pack-data fix/condition-pack-data-flag
fix-byte-list-equality fix/byte-list-equality
perf-group-cache perf/cache-overlay-groups
perf-deferred-fill a0f418c
perf-deferred-fill-peek 3958614
perf-deferred-fill-placed 037d531
perf-record-batches perf/record-batches -p:SafePatchBatches=true
combined safepatch-perf-experiment -p:SafePatchBatches=true
LIST
echo "done $(date +%T)"
