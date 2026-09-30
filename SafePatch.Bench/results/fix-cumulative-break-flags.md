# Set every later break flag when a full parse stops at a break

Branch `fix/cumulative-break-flags` (one commit on 0.54.4).

Break flags say that a record's data stops before a break, so the fields after it and after every later
break are absent. The overlay tests each break against the data's length and sets them all; `CreateFromBinary` set
only the break it stopped at. Example: `DefaultCombatstyle` (`00003D:Skyrim.esm`): `Flight.Versioning` is `Break2`
from the full parse and `Break2, Break3` from the overlay; `FXWorkingExampleProjectile` (`020E20:Skyrim.esm`):
`DATADataTypeState` is `Break0` against `Break0, Break1`. The same bytes are written either way, but a caller
asking `HasFlag(Break3)` of the full parse was told the fields after `Break3` were there. 60 generated lines change.

## Effect on reads

Records that `CreateFromBinary` and the overlay read differently, before and after:

| Record type | base | fix-cumulative-breaks |
| --- | ---: | ---: |
| CombatStyle | 47 | 0 |
| EffectShader | 9 | 0 |
| Explosion | 6 | 0 |
| Faction | 16 | 14 |
| Projectile | 4 | 0 |
| Weather | 1 | 0 |

The remaining Faction differences are gendered rank titles (see `fix/gendered-item-equality`).

## Cost

Reading is no slower. Full parse and overlay timings (unchanged code paths vary by about 10%):

| Workload | base | fix-cumulative-breaks | Change |
| --- | ---: | ---: | ---: |
| full parse (CreateFromBinary) of every plugin | 6,809 (5,954) | 6,378 (5,972) | -6% |
| every record's FormKey and EditorID | 1,080 (1,070) | 1,073 (1,062) | -1% |
| keep every record object | 3,039 (2,902) | 3,046 (2,881) | +0% |
| Heap holding every record object | 1,578 MiB | 1,578 MiB | |

## Tests

`BreakFlagsTests`: crime values stopping at the first break read as `Break0 | Break1` through both paths (the full parse fails before the fix).



## Setup

Measured on Skyrim SE 1.6.1170 with its base game and Creation Club plugins (82 plugins, 364 MiB,
1,252,863 records), on a Ryzen 7 9800X3D (8 cores, 16 threads), 126 GB RAM, NVMe SSD, Windows 11, .NET 10.0.301.
Each branch is built from 0.54.4 on its own, with the same `SafePatch.Bench`. Timings: three rounds, each running
every build once (so a busy spell on the machine is shared out rather than landing on one build), three runs of each
workload per round; the table gives the median of the rounds' medians, and the best run in brackets, in ms. Runs
of the same code vary by about 10%, more on a busy machine, so changes smaller than that are noise. Parity: every
record of Skyrim, Update, Dawnguard, HearthFires and Dragonborn (1,178,543) read by `CreateFromBinary` and by the
overlay (EditorID read first), compared with `Equals`.
