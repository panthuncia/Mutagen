# All branches together (`safepatch-perf-experiment`)

Every fix and performance branch merged onto 0.54.4, the generated code regenerated from the merged generator.

## Measurements

Timed in the same session as the three later fix branches, alternating 0.54.4 (`base-2`) and the combined build
(`combined-2`) round by round. The combined build then also deferred Skyrim's placed objects, since dropped from
`perf/deferred-overlay-fill` (no measurable gain; see that file); that cost about
65 MiB, so the heap holding every record is about that much smaller now, and it made no difference to the timings
beyond noise. The parity and hash checks below are of the
combined build as it stands.

| Workload | base-2 | combined-2 | Change |
| --- | ---: | ---: | ---: |
| open, then every record's FormKey and EditorID | 1,358 (1,232) | 786 (532) | -42% |
| every record's FormKey | 1,239 (1,222) | 275 (212) | -78% |
| every record's FormKey and EditorID | 1,274 (1,240) | 357 (345) | -72% |
| keep every record object | 3,258 (3,093) | 1,440 (1,323) | -56% |
| every record's FormKey and EditorID, within plugins in parallel | 375 (361) | 100 (89) | -73% |
| open, then the same within plugins in parallel | 364 (325) | 185 (173) | -49% |
| the same through EnumerateMajorRecordBatches |  | 95 (84) |  |
| 2,000 statics by key | 1,053 (1,017) | 1 (1) | -100% |
| full parse (CreateFromBinary) of every plugin | 7,165 (6,942) | 6,932 (6,464) | -3% |
| Scanner: every record's FormKey and EditorID | 150 (110) | 156 (106) | +4% |
| Heap holding every record object | 1,578 MiB | 1,170 MiB | |

## Parity

Records of the five masters that `CreateFromBinary` and the overlay read differently:

| Record type | base | combined |
| --- | ---: | ---: |
| Armor | 3,564 | 0 |
| ArmorAddon | 1,112 | 0 |
| AssociationType | 20 | 0 |
| CombatStyle | 47 | 0 |
| DialogResponses | 5 | 0 |
| DialogTopic | 3 | 0 |
| DialogView | 2 | 0 |
| EffectShader | 9 | 0 |
| Explosion | 6 | 0 |
| Faction | 16 | 0 |
| MaterialObject | 23 | 0 |
| Package | 5,081 | 0 |
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
| DialogView | 2 | 0 |
| Faction | 14 | 0 |
| MaterialObject | 23 | 0 |
| Package | 30 | 0 |
| Race | 204 | 0 |

The other 77 plugins (Creation Club content and one landscape mod, 74,320 records), read the same way:

| Record type | base-all | combined-all |
| --- | ---: | ---: |
| Armor | 453 | 0 |
| ArmorAddon | 476 | 0 |
| MaterialObject | 6 | 0 |
| Package | 354 | 0 |
| Quest | 88 | 0 |
| Race | 36 | 0 |
| Weapon | 6 | 0 |

Every record of the load order now reads the same through both paths and equals a second full parse of itself.

Records equal to the full parse (a second full parse, and the overlay) whose hash code differs:

| Record type | base | combined |
| --- | ---: | ---: |
| PlacedObject | 1,720,526 | 0 |
| Cell | 153,364 | 0 |
| Landscape | 108,146 | 0 |
| DialogResponses | 82,371 | 0 |
| NavigationMesh | 39,848 | 0 |
| DialogTopic | 39,685 | 0 |
| PlacedNpc | 25,956 | 0 |
| Npc | 13,050 | 0 |
| Static | 12,685 | 0 |
| Package | 10,111 | 0 |
| 67 other types | 84,546 | 0 |
| **All types** | **2,290,288** | **0** |
| (equal pairs checked) | 2,329,771 | 2,357,086 |

and across the other 77 plugins:

| Record type | base-all | combined-all |
| --- | ---: | ---: |
| PlacedObject | 115,706 | 0 |
| Cell | 2,796 | 0 |
| PlacedNpc | 2,644 | 0 |
| Static | 2,452 | 0 |
| ConstructibleObject | 2,200 | 0 |
| DialogResponses | 1,984 | 0 |
| Npc | 1,512 | 0 |
| DialogTopic | 1,338 | 0 |
| Weapon | 1,116 | 0 |
| MagicEffect | 1,112 | 0 |
| 57 other types | 9,074 | 0 |
| **All types** | **141,934** | **0** |
| (equal pairs checked) | 146,250 | 148,640 |

Concurrent reads: 0 differed from a single-threaded read.

## Setup

Measured on Skyrim SE 1.6.1170 with its base game and Creation Club plugins (82 plugins, 364 MiB,
1,252,863 records), on a Ryzen 7 9800X3D (8 cores, 16 threads), 126 GB RAM, NVMe SSD, Windows 11, .NET 10.0.301.
Each branch is built from 0.54.4 on its own, with the same `SafePatch.Bench`. Timings: three rounds, each running
every build once (so a busy spell on the machine is shared out rather than landing on one build), three runs of each
workload per round; the table gives the median of the rounds' medians, and the best run in brackets, in ms. Runs
of the same code vary by about 10%, more on a busy machine, so changes smaller than that are noise. Parity: every
record of Skyrim, Update, Dawnguard, HearthFires and Dragonborn (1,178,543) read by `CreateFromBinary` and by the
overlay (EditorID read first), compared with `Equals`.
