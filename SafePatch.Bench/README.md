# Lazy overlay experiment (branch `safepatch-perf-experiment`)

An experiment in making Mutagen's binary overlays read plugins the way SafePatch's raw scanner (after SARP's C++
reader) does: touch only what is asked for.

## What changed

1. **Deferred fill.** A major record's overlay is created from its header alone. Decompression and the walk over
   its subrecords run on the first read of a field (`PluginBinaryOverlay.EnsureFilled`), once, thread-safely.
   - Core: `_recordData` becomes a property that completes the fill; `DeferFill`, `EnsureFilled`.
   - Generator: location fields and fill-assigned properties are emitted through `LazyFill`, so reading one
     completes the fill; eligible records get a header-only factory plus a `{Name}Fill` method holding the old body.
   - Eligible: concrete major records with no hand-written parsing (no custom fields, custom ends, subgroups or
     custom triggers) in their class or its bases, plus `PlacedObject`, whose hand-written overlay code was
     audited to complete the fill before reading what it sets (`AuditedForDeferredFill`). `Npc`, `Cell`,
     `Worldspace`, `Quest`, `Race`, `Package`, `Perk`, `Region`, `Armor` and a few others stay eager.
2. **EditorID without a fill.** While a fill is pending, `EditorID` reads the first subrecord, inflating a
   compressed record only as far as that. A record whose first subrecord is not an EDID has none.
3. **Cached groups.** A mod overlay's group property was a new group on every access: the group's bytes copied
   out of the file and every record located again. It is now built once.
4. **Batches for parallel reads.** `SkyrimModMixIn.EnumerateMajorRecordBatches` yields a plugin's records in
   batches that can be read on different threads: each top-level group, the worldspaces with their persistent
   cells, and each block of interior or exterior cells. It is hand-written (a generated version would cover every
   game) and lists the mod's groups by reflection once. With the thread-safe fill, a single large plugin (Skyrim.esm
   holds 70% of the records) then spreads over every core, as the scanner's chunks do.

Only the Skyrim generator was run; `MajorRecord_Generated.cs` in Core was edited to match what the generator emits.

## Results (Skyrim SE 1.6, base game and Creation Club: 82 plugins, 364 MB, 1,252,863 records)

Best of five, each plugin on its own thread unless the row says otherwise.

| | Stock 0.54.4 | This branch | SafePatch scanner |
| --- | --- | --- | --- |
| Open, then every record's FormKey and EditorID | 1,114 ms | 365 ms | 100 ms (it reads the files) |
| The same, each plugin also split into batches | 606 ms | 169 ms | |
| Every record's FormKey, mods open | 1,147 ms | 184 ms | |
| Every record's FormKey and EditorID, mods open | 1,519 ms | 292 ms | |
| The same through `EnumerateMajorRecordBatches` | (939 ms, split by the caller) | 85 ms | |
| 2,000 statics by key | 1,045 ms | 1 ms | |
| Heap to hold every record object | 1,578 MiB | 1,170 MiB | |

On 2,002 small plugins the branch changes nothing: opening the overlays (headers and group tables) dominates.

## Checks

- Every record of Skyrim, Update, Dawnguard, HearthFires and Dragonborn (1,178,543) compared with Mutagen's full
  parse, reading the EditorID first: the branch differs in exactly the records stock 0.54.4 already does (known
  overlay/full-parse discrepancies), and nowhere else (`check`).
- 6.6 million concurrent reads of fresh Dawnguard and Dragonborn records, eight threads racing to complete each
  fill: identical to single-threaded reads (`check-threads`). This caught a torn read of `_structData` during a
  fill, fixed by leaving the header alone and peeking EditorIDs from an immutable copy.
- Reading every vanilla record through `EnumerateMajorRecordBatches` visits the same 1,252,863 records, with the
  same FormKeys and EditorIDs, as enumerating each plugin in turn.
- `Mutagen.Bethesda.UnitTests` (3,308) and `Mutagen.Bethesda.Core.UnitTests` (789) pass, on net9.0 and net10.0.

## Running

```sh
dotnet build SafePatch.Bench -c Release -p:DisableGitVersionTask=true -p:GeneratePackageOnBuild=false
dotnet SafePatch.Bench/bin/Release/net10.0/SafePatch.Bench.dll <Data folder> <plugins.txt> [runs]
dotnet SafePatch.Bench/bin/Release/net10.0/SafePatch.Bench.dll check <Data folder> Skyrim.esm,Update.esm
dotnet SafePatch.Bench/bin/Release/net10.0/SafePatch.Bench.dll check-threads <Data folder> Dawnguard.esm
```

`../Mutagen-stock` is a worktree of the untouched 0.54.4 tag with the same benchmark, for comparison.
