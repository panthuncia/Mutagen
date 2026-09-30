# The combined build pinned as 0.54.5-safepatch.6

Two more branches merged into `safepatch-perf-experiment` (`aaea3679c`), against the build pinned as
`0.54.5-safepatch.5` (`7f69d99e2`):

| Branch | Change |
| --- | --- |
| `perf/defer-audited-records` | Skyrim records whose hand-written overlay code was audited, and placed objects, defer their fields until one is read |
| `perf/record-index` | `PluginRecordIndex` and `LoadOrderRecordIndex`: a load order's records indexed from plugin bytes, without parsing records |

The audited types: Armor, ArmorAddon, CameraPath, DialogTopic, FootstepSet, Furniture, IdleMarker, MagicEffect, Npc,
Package, Perk, Quest, Race, Region, Weather and Worldspace. Each had its fill's hand-written members wrapped so that
reading one completes the fill.

The measurements are on the MO2 profile of `conflict-scan.md` (760 plugins, 2,562,847 record versions), Ryzen 7
9800X3D, Windows 11, .NET 10, server GC.

## Parity

- `check-first` reads each property first on a record fresh from its group (so no fill has run) and compares it with
  a full parse: 1,499,608 values of the 16 audited types and 58.7 million of placed objects, none differing.
- `check` and `check-threads` on the base game and DLC: none differing.
- `index-check` compares the record index with `EnumerateMajorRecordBatches`, scanned and loaded from a cache: all
  2,562,847 versions have the same FormKey, EditorID and parent, every plugin the same count, and all 341,074 winning
  EditorIDs are found as written, in upper case and in lower case.
- The fork's unit tests: 3,392 passed, 4 skipped, on .NET 9 and 10.

## Deferring the audited records

Reading every record's batches the first time in a process took 3.0-3.3 s before and about 1.95 s after. Keeping every
record of the vanilla plugins costs 1,111 MiB against 1,047 MiB (+64 MiB), and is faster too (3,897 ms to 2,332 ms).

## The record index

`record-index`, ms:

| | Plugins | Grouping | First EditorID lookup | Total | Allocated |
| --- | ---: | ---: | ---: | ---: | ---: |
| Scanned, first run in a process | 308 | 78 | 28 | 414 | 790 MiB |
| Scanned, later runs | 240-415 | 68-86 | 39-55 | 346-555 | 786 MiB |
| From the cache, first run in a process | 29-42 | 62-101 | 28-30 | 120-171 | 294 MiB |
| From the cache, later runs | 23-25 | 68-84 | 20-25 | 116-130 | 294 MiB |

The cache is one file per plugin (89 MB for the 760), kept while the plugin's size and last write time are
unchanged. The index built from `EnumerateMajorRecordBatches` took 2-3.5 s the first time in a process and 0.5-0.65 s
warm. All runs had the plugins in the OS file cache; a cold disk has not been measured.
