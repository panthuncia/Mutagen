# All branches together (`safepatch-perf-experiment`)

Every fix and performance branch merged onto 0.54.4, the generated code regenerated from the merged generator.

## Measurements

Timed in a separate session from the per-branch files, alternating 0.54.4 (`base-paired`) and the combined build
round by round:

| Workload | base-paired | combined | Change |
| --- | ---: | ---: | ---: |
| open, then every record's FormKey and EditorID | 1,156 (1,082) | 705 (396) | -39% |
| every record's FormKey | 1,068 (1,061) | 194 (179) | -82% |
| every record's FormKey and EditorID | 1,080 (1,074) | 289 (283) | -73% |
| keep every record object | 3,079 (2,922) | 1,370 (1,283) | -56% |
| every record's FormKey and EditorID, within plugins in parallel | 347 (306) | 100 (85) | -71% |
| open, then the same within plugins in parallel | 334 (300) | 170 (166) | -49% |
| the same through EnumerateMajorRecordBatches |  | 85 (83) |  |
| 2,000 statics by key | 941 (923) | 1 (1) | -100% |
| full parse (CreateFromBinary) of every plugin | 6,192 (6,081) | 6,361 (5,797) | +3% |
| Scanner: every record's FormKey and EditorID | 146 (103) | 143 (103) | -2% |
| Heap holding every record object | 1,578 MiB | 1,170 MiB | |

## Parity

Records that `CreateFromBinary` and the overlay read differently:

| Record type | base | combined |
| --- | ---: | ---: |
| Armor | 3,564 | 0 |
| ArmorAddon | 1,112 | 0 |
| AssociationType | 20 | 0 |
| CombatStyle | 47 | 0 |
| DialogResponses | 5 | 5 |
| DialogTopic | 3 | 3 |
| DialogView | 2 | 2 |
| EffectShader | 9 | 0 |
| Explosion | 6 | 0 |
| Faction | 16 | 0 |
| MaterialObject | 23 | 23 |
| Package | 5,081 | 59 |
| Projectile | 4 | 0 |
| Quest | 316 | 0 |
| Race | 204 | 0 |
| Static | 11,915 | 0 |
| Weapon | 18 | 0 |
| Weather | 1 | 0 |

Records unequal to a second `CreateFromBinary` of the same bytes:

| Record type | base | combined |
| --- | ---: | ---: |
| Armor | 3,564 | 0 |
| ArmorAddon | 1,112 | 0 |
| AssociationType | 20 | 0 |
| DialogView | 2 | 2 |
| Faction | 14 | 0 |
| MaterialObject | 23 | 23 |
| Package | 30 | 30 |
| Race | 204 | 0 |

Concurrent reads: 0 differed from a single-threaded read.

The differences left are not diagnosed yet: dialog responses, topics and views, material objects, and 59 packages
(30 of which are unequal even to a second full parse of themselves, so `Equals` is at fault there, not the reads).

## Setup

Measured on Skyrim SE 1.6.1170 with its base game and Creation Club plugins (82 plugins, 364 MiB,
1,252,863 records), on a Ryzen 7 9800X3D (8 cores, 16 threads), 126 GB RAM, NVMe SSD, Windows 11, .NET 10.0.301.
Each branch is built from 0.54.4 on its own, with the same `SafePatch.Bench`. Timings: three rounds, each running
every build once (so a busy spell on the machine is shared out rather than landing on one build), three runs of each
workload per round; the table gives the median of the rounds' medians, and the best run in brackets, in ms. Runs
of the same code vary by about 10%, more on a busy machine, so changes smaller than that are noise. Parity: every
record of Skyrim, Update, Dawnguard, HearthFires and Dragonborn (1,178,543) read by `CreateFromBinary` and by the
overlay (EditorID read first), compared with `Equals`.
