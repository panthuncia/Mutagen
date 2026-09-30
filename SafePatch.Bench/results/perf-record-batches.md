# `EnumerateMajorRecordBatches`: one mod on several threads

Branch `perf/record-batches` (one commit on 0.54.4).

`EnumerateMajorRecords` yields a mod's records one after another, so a caller can parallelise only across mods, and
Skyrim.esm (70% of a vanilla load order's records) runs on one core. The new extension yields the same records in
batches that can be read on different threads: each top-level group, the worldspaces with their persistent cells,
and each block of interior or exterior cells. It is hand-written for Skyrim.

## Measurements

On 0.54.4 alone (each plugin on its own thread, against each plugin split into batches):

| Workload | base | perf-record-batches | Change |
| --- | ---: | ---: | ---: |
| every record's FormKey and EditorID | 1,080 (1,070) | 1,078 (1,065) | -0% |
| every record's FormKey and EditorID, within plugins in parallel | 320 (303) | 334 (311) | +4% |
| the same through EnumerateMajorRecordBatches |  | 317 (294) |  |
| Heap holding every record object | 1,578 MiB | 1,578 MiB | |

The gain grows with the other changes: with deferred fills, per-record work is small enough that the single-core
plugin dominates (see `combined.md`).

## Parity and tests

Reads are unchanged. `RecordBatchesTests`: the batches hold exactly the records `EnumerateMajorRecords` yields, for a
mod with interior cells, a worldspace with persistent and exterior cells, and a topic with responses. Mutagen's unit
tests pass.

## Setup

Measured on Skyrim SE 1.6.1170 with its base game and Creation Club plugins (82 plugins, 364 MiB,
1,252,863 records), on a Ryzen 7 9800X3D (8 cores, 16 threads), 126 GB RAM, NVMe SSD, Windows 11, .NET 10.0.301.
Each branch is built from 0.54.4 on its own, with the same `SafePatch.Bench`. Timings: three rounds, each running
every build once (so a busy spell on the machine is shared out rather than landing on one build), three runs of each
workload per round; the table gives the median of the rounds' medians, and the best run in brackets, in ms. Runs
of the same code vary by about 10%, more on a busy machine, so changes smaller than that are noise. Parity: every
record of Skyrim, Update, Dawnguard, HearthFires and Dragonborn (1,178,543) read by `CreateFromBinary` and by the
overlay (EditorID read first), compared with `Equals`.
