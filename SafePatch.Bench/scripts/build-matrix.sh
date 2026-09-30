#!/usr/bin/env bash
# Builds SafePatch.Bench (from branch safepatch/bench) against each variant into $OUT/<name>.
source "$(dirname "$0")/env.sh"
worktree build 0188012
trap 'remove_worktrees build' EXIT
cd "$WORK/build"
while read -r name ref flags; do
  [ -z "$name" ] && continue
  echo "== $name ($ref) $(date +%T)"
  git checkout -q --detach "$ref" || { echo "checkout failed"; continue; }
  rm -rf SafePatch.Bench && mkdir -p SafePatch.Bench
  for f in Program.cs Diag.cs SafePatch.Bench.csproj; do git -C "$REPO" show "safepatch/bench:SafePatch.Bench/$f" > "SafePatch.Bench/$f"; done
  rm -rf "$OUT/$name"
  dotnet build SafePatch.Bench -c Release -o "$OUT/$name" -p:DisableGitVersionTask=true -p:GeneratePackageOnBuild=false \
    -p:SafePatchRepo="$REPO/../SynthesisMCP" $flags 2>&1 | grep -E " error |rror\(s\)" | sort -u | head -5
  rm -rf SafePatch.Bench
done <<'LIST'
base 0188012
fix-unsigned-enums fix/unsigned-two-byte-enums
fix-nullable-fallback fix/nullable-enum-fallback-parse
fix-overlay-defaults fix/overlay-declared-defaults
fix-cumulative-breaks fix/cumulative-break-flags
fix-gendered-equality fix/gendered-item-equality
fix-condition-pack-data fix/condition-pack-data-flag
fix-content-equality fix/content-equality
fix-overlay-float-epsilon fix/overlay-float-epsilon
perf-group-cache perf/cache-overlay-groups
perf-deferred-fill a0f418c
perf-deferred-fill-peek perf/deferred-overlay-fill
perf-deferred-fill-placed 037d531
perf-record-batches perf/record-batches -p:SafePatchBatches=true
combined safepatch-perf-experiment -p:SafePatchBatches=true
LIST
echo "done $(date +%T)"
