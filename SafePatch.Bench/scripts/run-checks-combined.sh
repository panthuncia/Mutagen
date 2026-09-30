#!/usr/bin/env bash
# Correctness checks for the combined build: parity, hash consistency and concurrent reads.
cd /c/Users/matth/source/repos/Mutagen-wt
D="D:/SteamLibrary/steamapps/common/Skyrim Special Edition/Data"
M=Skyrim.esm,Update.esm,Dawnguard.esm,HearthFires.esm,Dragonborn.esm
P="C:/Users/matth/AppData/Local/Temp/claude/c--Users-matth-source-repos-SynthesisMCP/488b0f26-3ebe-40c5-86ae-97a8c84a2d6c/scratchpad/bench/vanilla-plugins.txt"
ALL=$(grep -v '^#' "$P" | sed 's/^\*//; s/\r//' | grep -v '^$' | while read p; do [ -f "$D/$p" ] && echo "$p"; done | paste -sd,)
dotnet bench-out/combined/SafePatch.Bench.dll check "$D" $M combined > parity-combined3.log 2>&1
dotnet bench-out/combined/SafePatch.Bench.dll check "$D" "$ALL" combined-all > parity-all-combined.log 2>&1
dotnet bench-out/combined/SafePatch.Bench.dll check-hash "$D" $M combined > hash-combined.log 2>&1
dotnet bench-out/combined/SafePatch.Bench.dll check-hash "$D" "$ALL" combined-all > hash-all-combined.log 2>&1
{ echo "== threads combined"; dotnet bench-out/combined/SafePatch.Bench.dll check-threads "$D" Dawnguard.esm; } > threads-combined.log 2>&1
grep -h "differing from\|Unequal to\|hash differs\|concurrent" parity-combined3.log parity-all-combined.log hash-combined.log hash-all-combined.log threads-combined.log | sed -E 's/ in [^;]*;/;/' | cut -c1-200
