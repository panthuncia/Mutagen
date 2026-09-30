# Shared settings for the SafePatch.Bench scripts; each script sources this.
#   REPO   the Mutagen clone (the main working tree, whichever worktree the script runs from)
#   WORK   scratch space inside it, excluded from git: temporary worktrees, bench builds, logs of new runs
#   D, P   the Skyrim Data folder (SKYRIM_DATA to override) and the load order's plugins.txt
set -u
SCRIPTS=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO=$(cd "$(git -C "$SCRIPTS" rev-parse --git-common-dir)/.." && pwd)
WORK="$REPO/.safepatch-work"
OUT="$WORK/bench-out"
LOGS="$WORK/logs"
D="${SKYRIM_DATA:-D:/SteamLibrary/steamapps/common/Skyrim Special Edition/Data}"
P="$SCRIPTS/vanilla-plugins.txt"
M=Skyrim.esm,Update.esm,Dawnguard.esm,HearthFires.esm,Dragonborn.esm
mkdir -p "$OUT" "$LOGS"
grep -qx '/.safepatch-work/' "$REPO/.git/info/exclude" 2>/dev/null || echo '/.safepatch-work/' >> "$REPO/.git/info/exclude"

# Every plugin of the load order other than the masters, comma-separated.
all_plugins() {
  grep -v '^#' "$P" | sed 's/^\*//; s/\r//' | grep -v '^$' | while read -r p; do [ -f "$D/$p" ] && echo "$p"; done | paste -sd,
}

# A temporary worktree at $WORK/<name>, detached at <ref>; removed again by remove_worktrees.
worktree() {
  git -C "$REPO" worktree remove --force "$WORK/$1" 2>/dev/null
  git -C "$REPO" worktree add -q --detach "$WORK/$1" "$2"
}
remove_worktrees() {
  for w in "$@"; do git -C "$REPO" worktree remove --force "$WORK/$w" 2>/dev/null; done
  git -C "$REPO" worktree prune
}
