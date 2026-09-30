# Read a nullable enum's binary fallback back as null

Branch `fix/nullable-enum-fallback-parse` (one commit on 0.54.4).

Enums declared with `nullableBinaryFallback` (Skyrim's `WeaponData.Skill`, `BookSkill`, `Class` and
`WorkbenchData` skills, and their Oblivion and Fallout 3 equivalents; 13 fields) are written as the fallback, `-1`,
when null, and the overlay reads `-1` back as null. The full parse kept `(Skill)(-1)`. Example:
`dunMarkarthWizardSpiderControlStaffFake` (`0C19FF:Skyrim.esm`): `Data.Skill` is `-1` from `CreateFromBinary`,
null from the overlay. The generated copy-in now maps the fallback to null.

## Effect on reads

Records that `CreateFromBinary` and the overlay read differently, before and after:

| Record type | base | fix-nullable-fallback |
| --- | ---: | ---: |
| Weapon | 18 | 0 |



## Cost

Reading is no slower. Full parse and overlay timings (unchanged code paths vary by about 10%):

| Workload | base | fix-nullable-fallback | Change |
| --- | ---: | ---: | ---: |
| full parse (CreateFromBinary) of every plugin | 6,809 (5,954) | 6,422 (5,889) | -6% |
| every record's FormKey and EditorID | 1,080 (1,070) | 1,076 (1,062) | -0% |
| keep every record object | 3,039 (2,902) | 3,081 (2,905) | +1% |
| Heap holding every record object | 1,578 MiB | 1,578 MiB | |

## Tests

`NullableEnumFallbackTests`: a weapon written with a null skill reads back null through both paths (the full parse fails before the fix).



## Setup

Measured on Skyrim SE 1.6.1170 with its base game and Creation Club plugins (82 plugins, 364 MiB,
1,252,863 records), on a Ryzen 7 9800X3D (8 cores, 16 threads), 126 GB RAM, NVMe SSD, Windows 11, .NET 10.0.301.
Each branch is built from 0.54.4 on its own, with the same `SafePatch.Bench`. Timings: three rounds, each running
every build once (so a busy spell on the machine is shared out rather than landing on one build), three runs of each
workload per round; the table gives the median of the rounds' medians, and the best run in brackets, in ms. Runs
of the same code vary by about 10%, more on a busy machine, so changes smaller than that are noise. Parity: every
record of Skyrim, Update, Dawnguard, HearthFires and Dragonborn (1,178,543) read by `CreateFromBinary` and by the
overlay (EditorID read first), compared with `Equals`.
