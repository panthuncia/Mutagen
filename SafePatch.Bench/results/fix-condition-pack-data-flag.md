# Keep a condition's pack-data bit out of Flags in the overlay

Branch `fix/condition-pack-data-flag` (one commit on 0.54.4).

In Skyrim and Starfield, two of a condition's flag bits, "use aliases" (`0x02`) and "use pack data" (`0x08`),
are exposed on its data (`UseAliases`, `UsePackageData`), not in `Condition.Flags`, whose enum does not define them.
`CreateFromBinary` clears both from `Flags`; the overlay cleared only the aliases bit. Example: `WERJ02PlayerThief`
(`0B8152:Skyrim.esm`): its first response's fifth condition has `Flags` `OR` from the full parse and `9` (`OR` and
the undefined `0x08`) from the overlay; `HoldPositionWithTravel1024` (`10FAAF:Skyrim.esm`): a procedure's condition
has `0` against `8`. Besides breaking equality, a condition copied from the overlay kept the bit in `Flags`, so the
writer, which ORs `UsePackageData` into `Flags`, wrote it even after `UsePackageData` was turned off. The overlay now
clears the bit as the full parse does. Fallout 4 keeps these bits in its `Flag` enum and is unaffected.

## Effect on reads

Records that `CreateFromBinary` and the overlay read differently, before and after:

| Record type | base | fix-condition-pack-data |
| --- | ---: | ---: |
| DialogResponses | 5 | 0 |
| DialogTopic | 3 | 0 |
| Package | 5,081 | 5,071 |

Most packages with such conditions also differ by two-byte enums (see `fix/unsigned-two-byte-enums`), so the
package count falls by only 10 here. Of the 59 packages that fix leaves, 53 have such conditions and the other 6
differ only by the lists of `fix/content-equality`; with every branch merged, no package differs (`combined.md`).

## Cost

Reading is no slower. Full parse and overlay timings (unchanged code paths vary by about 10%):

| Workload | base-2 | fix-condition-pack-data | Change |
| --- | ---: | ---: | ---: |
| full parse (CreateFromBinary) of every plugin | 7,165 (6,942) | 7,068 (6,471) | -1% |
| every record's FormKey and EditorID | 1,274 (1,240) | 1,267 (1,198) | -1% |
| keep every record object | 3,258 (3,093) | 3,261 (3,110) | +0% |
| Heap holding every record object | 1,578 MiB | 1,578 MiB | |

## Tests

`ConditionPackDataFlagTests` (Skyrim and Starfield): a condition written with `UsePackageData` reads back with `Flags` of `OR` alone and `UsePackageData` set, through both paths (the overlay fails before the fix).

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
