# Compare and hash lists and byte arrays by content

Branch `fix/content-equality` (two commits on 0.54.4).

Two commits, one for each generated member:

1. **`Equals` on lists of byte arrays.** A list of byte arrays holds memory slices, and the generated `Equals`
   compared the lists with `SequenceEqualNullable`, which uses the slice's own `Equals`: where it points, not its
   bytes. So a record with a non-empty list of byte arrays was unequal to every other instance of itself, including
   a second `CreateFromBinary` of the same bytes. Seven fields have this shape: Skyrim's `MaterialObject.DNAMs`,
   `DialogView.TNAMs` and `PackageBranch.Unknown`, the same three in Fallout 4, and Starfield's
   `NavigationMeshObstacleManagerSubObject` field. Example: `SnowMaterialGlacier` (`05E3F0:Skyrim.esm`) and
   `MQ202ViewShared` (`06C866:Skyrim.esm`) are unequal to themselves read twice, with every property equal by value.
   The equals mask already compared the elements by content (which is why `GetEqualsMask` reported no difference);
   `Equals` now does the same.
2. **`GetHashCode` on lists, dictionaries, 2D arrays and byte arrays.** The generated hash added these fields as
   objects, whose hash codes are the object's, while `Equals` compares their contents. So records equal by `Equals`
   hashed differently whenever they held any of these, which is most record types, and so did their parts: across
   two reads of Skyrim.esm, 985,151 of 1,262,207 equal list elements (faction ranks, perks, effects, quest aliases,
   cell references) had different hash codes. Anything that keys a dictionary or set by records or their parts found
   equal ones missing. The hash now adds their contents (`ContentHashExt.AddContents`; order-independent for
   dictionaries). 894 generated files change, each only in `GetHashCode`.

The generator gains `MutagenListType`, `MutagenArrayType`, `MutagenArray2dType` and `MutagenByteArrayType`, which
Mutagen now uses in place of Loqui's (as it already did for `Dict`), overriding only these members. The plugin
translation module registers its generators for them, and the mask module its 2D-array one.

Records equal to the full parse (a second full parse, and the overlay) whose hash code differs, in the five masters:

| Record type | base | fix-content-equality |
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
| (equal pairs checked) | 2,329,771 | 2,329,848 |

(Records unequal to their twin, such as those with gendered fields before `fix/gendered-item-equality`, are not
counted: the hash code is only required to agree for equal records. See `combined.md` for all branches together.)

## Effect on reads

Records that `CreateFromBinary` and the overlay read differently, before and after:

| Record type | base | fix-content-equality |
| --- | ---: | ---: |
| DialogView | 2 | 0 |
| MaterialObject | 23 | 3 |

Records unequal to a second `CreateFromBinary` of the same bytes:

| Record type | base | fix-content-equality |
| --- | ---: | ---: |
| DialogView | 2 | 0 |
| MaterialObject | 23 | 0 |
| Package | 30 | 0 |

3 material objects still differ between the reads here: their break flags, fixed by
`fix/cumulative-break-flags` (where the list hid them).

## Cost

Reading is no slower. Full parse and overlay timings (unchanged code paths vary by about 10%):

| Workload | base-2 | fix-content-equality | Change |
| --- | ---: | ---: | ---: |
| full parse (CreateFromBinary) of every plugin | 7,165 (6,942) | 6,892 (6,326) | -4% |
| every record's FormKey and EditorID | 1,274 (1,240) | 1,238 (1,214) | -3% |
| keep every record object | 3,258 (3,093) | 3,327 (3,069) | +2% |
| Heap holding every record object | 1,578 MiB | 1,578 MiB | |

## Tests

`ByteArrayListEqualityTests`: two material objects with equal `DNAMs` contents are equal, and unequal once one byte differs; a material object written and read equals a second full parse and its overlay (all fail before the fix). `ContentHashCodeTests`: an NPC with factions and keywords, a spell with a conditioned effect and a material object each hash alike across two full parses and the overlay; list contents hash in order, dictionaries in any order.

Timed in a later session than the branches above, alongside 0.54.4 again (`base-2`).

## Setup

Measured on Skyrim SE 1.6.1170 with its base game and Creation Club plugins (82 plugins, 364 MiB,
1,252,863 records), on a Ryzen 7 9800X3D (8 cores, 16 threads), 126 GB RAM, NVMe SSD, Windows 11, .NET 10.0.301.
Each branch is built from 0.54.4 on its own, with the same `SafePatch.Bench`. Timings: three rounds, each running
every build once (so a busy spell on the machine is shared out rather than landing on one build), three runs of each
workload per round; the table gives the median of the rounds' medians, and the best run in brackets, in ms. Runs
of the same code vary by about 10%, more on a busy machine, so changes smaller than that are noise. Parity: every
record of Skyrim, Update, Dawnguard, HearthFires and Dragonborn (1,178,543) read by `CreateFromBinary` and by the
overlay (EditorID read first), compared with `Equals`.
