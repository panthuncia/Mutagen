#!/usr/bin/env bash
# Builds SafePatch.Bench against 0.54.4 (base-3) and the combined build with the generated batches (combined-3), then
# times them round-robin as run-timing.sh does. Output: $LOGS/timing-generated-batches.log.
source "$(dirname "$0")/env.sh"
worktree build 0188012
trap 'remove_worktrees build' EXIT
cd "$WORK/build"
while read -r name ref flags; do
  echo "== build $name ($ref) $(date +%T)"
  git checkout -q --detach "$ref" || exit 1
  rm -rf SafePatch.Bench && mkdir -p SafePatch.Bench
  for f in Program.cs Diag.cs SafePatch.Bench.csproj; do git -C "$REPO" show "safepatch/bench:SafePatch.Bench/$f" > "SafePatch.Bench/$f"; done
  rm -rf "$OUT/$name"
  dotnet build SafePatch.Bench -c Release -o "$OUT/$name" -p:DisableGitVersionTask=true -p:GeneratePackageOnBuild=false \
    -p:SafePatchRepo="$REPO/../SynthesisMCP" $flags 2>&1 | grep -E " error |rror\(s\)" | sort -u | head -5
  rm -rf SafePatch.Bench
done <<'LIST'
base-3 0188012
combined-3 safepatch-perf-experiment -p:SafePatchBatches=true
LIST
for round in 1 2 3; do
  for v in base-3 combined-3; do
    echo "== round $round $v $(date +%T)"
    dotnet "$OUT/$v/SafePatch.Bench.dll" "$D" "$P" 3 - "$v"
  done
done > "$LOGS/timing-generated-batches.log" 2>&1
echo "done $(date +%T)" >> "$LOGS/timing-generated-batches.log"
