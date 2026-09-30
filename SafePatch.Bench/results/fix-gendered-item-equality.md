# Compare gendered items by value

Branch `fix/gendered-item-equality` (one commit on 0.54.4).

`GenderedItem<T>`, the overlay's `GenderedItemBinaryOverlay<T>` and Skyrim's
`ArmorAddonWeightSliderContainer` did not override `Equals`, and generated record `Equals` compares gendered fields
with `object.Equals`. So every record with a gendered field (armor, armor addons, races, factions' rank titles,
association types) was unequal to every other instance of itself, including a second `CreateFromBinary` of the same
bytes, while `GetEqualsMask` said they were equal. They now compare their male and female items, with a matching hash
code, and the comparison holds across implementations whose declared item types differ (an object's
`GenderedItem<ArmorModel?>` against an overlay's `IGenderedItemGetter<IArmorModelGetter?>`).

## Effect on reads

Records that `CreateFromBinary` and the overlay read differently, before and after:

| Record type | base | fix-gendered-equality |
| --- | ---: | ---: |
| Armor | 3,564 | 0 |
| ArmorAddon | 1,112 | 0 |
| AssociationType | 20 | 0 |
| Faction | 16 | 2 |
| Race | 204 | 0 |

Records unequal to a second `CreateFromBinary` of the same bytes:

| Record type | base | fix-gendered-equality |
| --- | ---: | ---: |
| Armor | 3,564 | 0 |
| ArmorAddon | 1,112 | 0 |
| AssociationType | 20 | 0 |
| Faction | 14 | 0 |
| Race | 204 | 0 |

This fix also exposed a bug in the deferred fill (gendered fields read before the fill), fixed on `perf/deferred-overlay-fill`.

## Cost

Reading is no slower. Full parse and overlay timings (unchanged code paths vary by about 10%):

| Workload | base | fix-gendered-equality | Change |
| --- | ---: | ---: | ---: |
| full parse (CreateFromBinary) of every plugin | 6,809 (5,954) | 6,501 (6,074) | -5% |
| every record's FormKey and EditorID | 1,080 (1,070) | 1,079 (1,071) | -0% |
| keep every record object | 3,039 (2,902) | 3,069 (2,897) | +1% |
| Heap holding every record object | 1,578 MiB | 1,578 MiB | |

## Tests

`GenderedItemEqualityTests`: two gendered items with the same values are equal with equal hashes; an armor addon equals a second full parse and its overlay; an armor with gendered models equals its overlay both ways round (all fail before the fix).



## Setup

Measured on Skyrim SE 1.6.1170 with its base game and Creation Club plugins (82 plugins, 364 MiB,
1,252,863 records), on a Ryzen 7 9800X3D (8 cores, 16 threads), 126 GB RAM, NVMe SSD, Windows 11, .NET 10.0.301.
Each branch is built from 0.54.4 on its own, with the same `SafePatch.Bench`. Timings: three rounds, each running
every build once (so a busy spell on the machine is shared out rather than landing on one build), three runs of each
workload per round; the table gives the median of the rounds' medians, and the best run in brackets, in ms. Runs
of the same code vary by about 10%, more on a busy machine, so changes smaller than that are noise. Parity: every
record of Skyrim, Update, Dawnguard, HearthFires and Dragonborn (1,178,543) read by `CreateFromBinary` and by the
overlay (EditorID read first), compared with `Equals`.
