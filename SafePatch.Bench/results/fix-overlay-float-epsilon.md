# Read float.Epsilon as zero in overlays, as the full parse does

Branch `fix/overlay-float-epsilon` (one commit on 0.54.4).

Files store some zeros as `float.Epsilon` (1E-45, the bits `01 00 00 00`). `CreateFromBinary` reads it as
zero and the writer writes zero for it, but overlays read the raw value. `Equals` compares floats within a tolerance
and does not notice; `==` and `GetHashCode` do. Example: `FireStormExplosion02` (`0877F9:Skyrim.esm`) and three
other explosions have `ISRadius` 0 from the full parse and 1E-45 from the overlay. It showed once
`fix/content-equality` made hash codes comparable, as the only records in the merged branches that were equal but
hashed differently.

Overlay float getters, generated (564 files) and hand-written (condition comparison values, navmesh data), now read
through `FloatBinaryTranslation.GetFloat`, which applies the full parse's rule, and so do `P2Float`'s and `P3Float`'s
span reads. Integer-backed floats already did. Placed primitives' and worldspaces' bounds read the raw value in both
paths, and stay so.

Records equal to the full parse whose hash code differs, with `fix/content-equality` and without:

| Record type | base | fix-content-equality | fix-overlay-float-epsilon | combined |
| --- | ---: | ---: | ---: | ---: |
| PlacedObject | 1,720,526 | 0 | 1,720,526 | 0 |
| Cell | 153,364 | 0 | 153,364 | 0 |
| Landscape | 108,146 | 0 | 108,146 | 0 |
| DialogResponses | 82,371 | 0 | 82,371 | 0 |
| NavigationMesh | 39,848 | 0 | 39,848 | 0 |
| DialogTopic | 39,685 | 0 | 39,685 | 0 |
| PlacedNpc | 25,956 | 0 | 25,956 | 0 |
| Npc | 13,050 | 0 | 13,050 | 0 |
| Static | 12,685 | 0 | 12,685 | 0 |
| Package | 10,111 | 0 | 10,111 | 0 |
| 67 other types | 84,546 | 0 | 84,546 | 0 |
| **All types** | **2,290,288** | **0** | **2,290,288** | **0** |
| (equal pairs checked) | 2,329,771 | 2,329,848 | 2,329,771 | 2,357,086 |

On this branch alone, lists and byte arrays still hash by reference (`fix/content-equality`), and the four
explosions are not counted: until `fix/cumulative-break-flags` their break flags make them unequal to the overlay. With
every branch merged (`combined`), no equal records hash differently; without this fix, those four did.

## Effect on reads

Records that `CreateFromBinary` and the overlay read differently, before and after:

none; the same records differ, by the same counts, as on 0.54.4.

The parity check compares with `Equals`, whose tolerance hides this difference, so its counts do not change.

## Cost

Reading is no slower. Full parse and overlay timings (unchanged code paths vary by about 10%):

| Workload | base-2 | fix-overlay-float-epsilon | Change |
| --- | ---: | ---: | ---: |
| full parse (CreateFromBinary) of every plugin | 7,165 (6,942) | 7,133 (6,462) | -0% |
| every record's FormKey and EditorID | 1,274 (1,240) | 1,244 (1,208) | -2% |
| keep every record object | 3,258 (3,093) | 3,206 (3,031) | -2% |
| Heap holding every record object | 1,578 MiB | 1,578 MiB | |

## Tests

`FloatEpsilonTests`: an explosion radius and a condition comparison value stored as `float.Epsilon` read as zero through both paths, and the explosion hashes alike (fails before the fix).

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
