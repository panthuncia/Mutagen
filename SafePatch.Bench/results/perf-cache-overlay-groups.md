# Build each group of a mod overlay once

Branch `perf/cache-overlay-groups` (one commit on 0.54.4).

A mod overlay's group getters (`mod.Statics`, `mod.Npcs`, ...) built a new group overlay on every access, locating
the group's records again. Code that reaches a group per record (`TryGetValue` in a loop, or a link cache resolving
through it) paid that every time. Each group overlay is now built on first access and kept for the mod's lifetime.

## Measurements

| Workload | base | perf-group-cache | Change |
| --- | ---: | ---: | ---: |
| open, then every record's FormKey and EditorID | 1,131 (1,073) | 1,136 (1,110) | +0% |
| every record's FormKey | 1,074 (1,055) | 1,013 (1,008) | -6% |
| every record's FormKey and EditorID | 1,080 (1,070) | 1,025 (1,020) | -5% |
| keep every record object | 3,039 (2,902) | 3,050 (2,909) | +0% |
| every record's FormKey and EditorID, within plugins in parallel | 320 (303) | 253 (226) | -21% |
| open, then the same within plugins in parallel | 317 (301) | 321 (300) | +1% |
| 2,000 statics by key | 952 (920) | 1 (1) | -100% |
| full parse (CreateFromBinary) of every plugin | 6,809 (5,954) | 6,346 (5,832) | -7% |
| Scanner: every record's FormKey and EditorID | 142 (97) | 148 (106) | +4% |
| Heap holding every record object | 1,578 MiB | 1,590 MiB | |

Looking up 2,000 statics by FormKey through `mod.Statics.TryGetValue` goes from about a second to about a
millisecond; everything else is unchanged within noise. The cost is the group overlays kept in memory once touched
(the heap row: every group touched once).

## Parity and tests

Reads are unchanged: the parity check matches 0.54.4 exactly on all 1,178,543 records. Mutagen's unit tests pass
(3,308 and 789). The change is in the generator (`LoquiBinaryTranslationGeneration`) and every game's mod overlay.

## Setup

Measured on Skyrim SE 1.6.1170 with its base game and Creation Club plugins (82 plugins, 364 MiB,
1,252,863 records), on a Ryzen 7 9800X3D (8 cores, 16 threads), 126 GB RAM, NVMe SSD, Windows 11, .NET 10.0.301.
Each branch is built from 0.54.4 on its own, with the same `SafePatch.Bench`. Timings: three rounds, each running
every build once (so a busy spell on the machine is shared out rather than landing on one build), three runs of each
workload per round; the table gives the median of the rounds' medians, and the best run in brackets, in ms. Runs
of the same code vary by about 10%, more on a busy machine, so changes smaller than that are noise. Parity: every
record of Skyrim, Update, Dawnguard, HearthFires and Dragonborn (1,178,543) read by `CreateFromBinary` and by the
overlay (EditorID read first), compared with `Equals`.
