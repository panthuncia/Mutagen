#!/usr/bin/env bash
# Builds SafePatch.Bench against 0.54.4 (base-4) and the combined build pinned as 0.54.5-safepatch.4 (combined-4), then
# runs each memory scenario in a process of its own, three rounds alternating the builds. Output: $LOGS/memory.log.
source "$(dirname "$0")/env.sh"
worktree build 0188012
trap 'remove_worktrees build' EXIT
cd "$WORK/build"
while read -r name ref; do
  echo "== build $name ($ref) $(date +%T)"
  git checkout -q --detach "$ref" || exit 1
  rm -rf SafePatch.Bench && mkdir -p SafePatch.Bench
  for f in Program.cs Diag.cs Memory.cs SafePatch.Bench.csproj; do git -C "$REPO" show "safepatch/bench:SafePatch.Bench/$f" > "SafePatch.Bench/$f"; done
  rm -rf "$OUT/$name"
  dotnet build SafePatch.Bench -c Release -o "$OUT/$name" -p:DisableGitVersionTask=true -p:GeneratePackageOnBuild=false \
    -p:SafePatchRepo="$REPO/../SynthesisMCP" 2>&1 | grep -E " error |rror\(s\)" | sort -u | head -5
  rm -rf SafePatch.Bench
done <<'LIST'
base-4 0188012
combined-4 7c093038d
LIST
for round in 1 2 3; do
  for scenario in open keep keep-read full; do
    for v in base-4 combined-4; do
      echo "== round $round $scenario $v $(date +%T)"
      dotnet "$OUT/$v/SafePatch.Bench.dll" memory "$D" "$P" "$scenario" "$v"
    done
  done
done > "$LOGS/memory.log" 2>&1
echo "done $(date +%T)" >> "$LOGS/memory.log"
