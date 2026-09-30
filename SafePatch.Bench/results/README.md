# SafePatch.Bench results

One file per branch, each measured on its own against 0.54.4, and the branches combined:

- [`fix/unsigned-two-byte-enums`](fix-unsigned-two-byte-enums.md): Read two-byte enums unsigned
- [`fix/enum-parsevalue-result`](fix-enum-parsevalue-result.md): Return the parsed value from EnumBinaryTranslation.ParseValue(reader)
- [`fix/nullable-enum-fallback-parse`](fix-nullable-enum-fallback-parse.md): Read a nullable enum's binary fallback back as null
- [`fix/overlay-declared-defaults`](fix-overlay-declared-defaults.md): Give overlay fields past a break the default a full parse gives them
- [`fix/cumulative-break-flags`](fix-cumulative-break-flags.md): Set every later break flag when a full parse stops at a break
- [`fix/gendered-item-equality`](fix-gendered-item-equality.md): Compare gendered items by value
- [`fix/condition-pack-data-flag`](fix-condition-pack-data-flag.md): Keep a condition's pack-data bit out of Flags in the overlay
- [`fix/content-equality`](fix-content-equality.md): Compare and hash lists and byte arrays by content
- [`fix/overlay-float-epsilon`](fix-overlay-float-epsilon.md): Read float.Epsilon as zero in overlays, as the full parse does
- [`perf/cache-overlay-groups`](perf-cache-overlay-groups.md): Build each group of a mod overlay once
- [`perf/deferred-overlay-fill`](perf-deferred-overlay-fill.md): Defer an overlay record's fill until a field is read
- [`perf/record-batches`](perf-record-batches.md): EnumerateMajorRecordBatches: one mod on several threads
- [All combined](combined.md)
- [The combined build with generated batches](generated-batches.md), timed against 0.54.4 only
- [Memory](memory.md): retained heap and process peaks, 0.54.4 against the combined build
- [`perf/mutable-link-cache-lookups`](perf-mutable-link-cache-lookups.md): Mutable link caches look records up in their mod's groups (issue 229)

## Setup

Measured on Skyrim SE 1.6.1170 with its base game and Creation Club plugins (82 plugins, 364 MiB,
1,252,863 records), on a Ryzen 7 9800X3D (8 cores, 16 threads), 126 GB RAM, NVMe SSD, Windows 11, .NET 10.0.301.
Each branch is built from 0.54.4 on its own, with the same `SafePatch.Bench`. Timings: three rounds, each running
every build once (so a busy spell on the machine is shared out rather than landing on one build), three runs of each
workload per round; the table gives the median of the rounds' medians, and the best run in brackets, in ms. Runs
of the same code vary by about 10%, more on a busy machine, so changes smaller than that are noise. Parity: every
record of Skyrim, Update, Dawnguard, HearthFires and Dragonborn (1,178,543) read by `CreateFromBinary` and by the
overlay (EditorID read first), compared with `Equals`.

Reproduce with `build-matrix.sh`, `run-bench.sh` and `run-timing.sh` beside this folder (see `../README.md`).
