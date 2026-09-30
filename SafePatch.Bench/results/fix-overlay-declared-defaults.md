# Give overlay fields past a break the default a full parse gives them

Branch `fix/overlay-declared-defaults` (one commit on 0.54.4).

When a record's data stops at a break, the fields after it are not in the file. `CreateFromBinary` leaves
them at their defaults, as a new object has them; the overlay returned `default(T)` or an empty array. Examples:
`LoadScreenMRaltar01` (`000E87:Skyrim.esm`) has an 8-byte `DNAM` that stops after the material, and its `Unused` is
`[0, 0, 0]` from the full parse and `[]` from the overlay (9,720 statics in Skyrim.esm alone); `DA08EbonyBladeTracking`
(`10FAEC:Skyrim.esm`) has script data without fragments, and its `ExtraBindDataVersion` is 2 (the declared default)
against 0. Overlay getters now return the field's declared default, and zeros of the field's length for fixed-length
byte arrays, as the overlay's struct path already did. 38 generated files change across the games.

## Effect on reads

Records that `CreateFromBinary` and the overlay read differently, before and after:

| Record type | base | fix-overlay-defaults |
| --- | ---: | ---: |
| Quest | 316 | 173 |
| Static | 11,915 | 0 |

The remaining Quest differences are two-byte enums (see `fix/unsigned-two-byte-enums`).

## Cost

Reading is no slower. Full parse and overlay timings (unchanged code paths vary by about 10%):

| Workload | base | fix-overlay-defaults | Change |
| --- | ---: | ---: | ---: |
| full parse (CreateFromBinary) of every plugin | 6,809 (5,954) | 6,326 (5,740) | -7% |
| every record's FormKey and EditorID | 1,080 (1,070) | 1,076 (1,064) | -0% |
| keep every record object | 3,039 (2,902) | 3,053 (2,918) | +0% |
| Heap holding every record object | 1,578 MiB | 1,578 MiB | |

## Tests

`OverlayDefaultsTests`: a static stopping at the break has `Unused` of three zero bytes, and a quest adapter without fragments has `ExtraBindDataVersion` 2, through both paths (the overlay fails before the fix).



## Setup

Measured on Skyrim SE 1.6.1170 with its base game and Creation Club plugins (82 plugins, 364 MiB,
1,252,863 records), on a Ryzen 7 9800X3D (8 cores, 16 threads), 126 GB RAM, NVMe SSD, Windows 11, .NET 10.0.301.
Each branch is built from 0.54.4 on its own, with the same `SafePatch.Bench`. Timings: three rounds, each running
every build once (so a busy spell on the machine is shared out rather than landing on one build), three runs of each
workload per round; the table gives the median of the rounds' medians, and the best run in brackets, in ms. Runs
of the same code vary by about 10%, more on a busy machine, so changes smaller than that are noise. Parity: every
record of Skyrim, Update, Dawnguard, HearthFires and Dragonborn (1,178,543) read by `CreateFromBinary` and by the
overlay (EditorID read first), compared with `Equals`.
