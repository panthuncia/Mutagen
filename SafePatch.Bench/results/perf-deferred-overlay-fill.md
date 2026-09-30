# Defer an overlay record's fill until a field is read

Branch `perf/deferred-overlay-fill` (one commit on 0.54.4). A major record's overlay is created from its header
and runs decompression and the subrecord walk on the first read of anything else, once and thread-safely; the
generator routes every member the fill sets through `LazyFill`. While the fill is pending, the EditorID comes from the
first subrecord, inflating a compressed record only that far (`InflatePrefix`).

The branch was measured as three steps, and the columns below keep them apart:

1. `perf-deferred-fill`: the fill alone.
2. `perf-deferred-fill-peek`: with the EditorID peek. This is the branch as it stands.
3. `perf-deferred-fill-placed`: also deferring Skyrim's placed objects, after auditing their hand-written overlay
   code. Measured and dropped (below).

## Measurements

| Workload | base | perf-deferred-fill | perf-deferred-fill-peek | perf-deferred-fill-placed | Change |
| --- | ---: | ---: | ---: | ---: | ---: |
| open, then every record's FormKey and EditorID | 1,131 (1,073) | 1,065 (872) | 964 (387) | 681 (391) | -40% |
| every record's FormKey | 1,074 (1,055) | 254 (247) | 261 (252) | 251 (235) | -77% |
| every record's FormKey and EditorID | 1,080 (1,070) | 837 (820) | 361 (349) | 344 (339) | -68% |
| keep every record object | 3,039 (2,902) | 1,315 (1,264) | 1,306 (1,251) | 1,323 (1,253) | -56% |
| every record's FormKey and EditorID, within plugins in parallel | 320 (303) | 277 (263) | 177 (162) | 166 (162) | -48% |
| open, then the same within plugins in parallel | 317 (301) | 261 (247) | 178 (164) | 176 (160) | -44% |
| 2,000 statics by key | 952 (920) | 938 (933) | 959 (923) | 963 (927) | +1% |
| full parse (CreateFromBinary) of every plugin | 6,809 (5,954) | 6,684 (5,801) | 6,255 (6,048) | 6,191 (5,844) | -9% |
| Scanner: every record's FormKey and EditorID | 142 (97) | 140 (113) | 141 (109) | 143 (115) | +1% |
| Heap holding every record object | 1,578 MiB | 1,094 MiB | 1,094 MiB | 1,159 MiB | |

- The fill alone makes enumerating records for their FormKeys about four times faster and the heap holding every
  record about a third smaller, since records nobody reads are never parsed.
- The EditorID peek is what makes FormKey-and-EditorID enumeration fast: without it, reading the EditorID completes
  the fill.
- **Deferring placed objects is not justified by these numbers.** Against the commit before it, its best runs are
  the same (the open-and-read workload's medians swing by hundreds of milliseconds between rounds, so its median gain
  is not to be trusted), and it holds 65 MiB more: a deferred-state object for each of the 916,540 placed objects,
  which were cheap to fill eagerly. The branch leaves placed objects eager.

## Parity

Records `CreateFromBinary` and the overlay read differently, against 0.54.4: none; the same records differ, by the same counts, as on 0.54.4. Concurrent reads (eight
threads racing to complete each fresh record's fill): 0 differed from a single-threaded read.

An earlier version of this branch read gendered fields, overflow lengths and a few other members the fill sets
without completing it. The parity check hid that, because gendered records never compared equal (see
`fix/gendered-item-equality`); merging the two showed it. Every member the fill sets now goes through `LazyFill`, and
a scan of the generated overlays of every game finds none that does not.

## Tests

Mutagen's unit tests pass (3,312 and 802, including the new ones). New: `DeferredFillTests` (gendered and loqui fields
read first), `EditorIDPeekTests` (plain and compressed records), `InflatePrefixTests` (every prefix of stored, fixed
and dynamic blocks against `ZLibStream`).

Only Skyrim has been checked against real plugins; the other games' deferred records are regenerated, compile and
pass Mutagen's tests.

## Setup

Measured on Skyrim SE 1.6.1170 with its base game and Creation Club plugins (82 plugins, 364 MiB,
1,252,863 records), on a Ryzen 7 9800X3D (8 cores, 16 threads), 126 GB RAM, NVMe SSD, Windows 11, .NET 10.0.301.
Each branch is built from 0.54.4 on its own, with the same `SafePatch.Bench`. Timings: three rounds, each running
every build once (so a busy spell on the machine is shared out rather than landing on one build), three runs of each
workload per round; the table gives the median of the rounds' medians, and the best run in brackets, in ms. Runs
of the same code vary by about 10%, more on a busy machine, so changes smaller than that are noise. Parity: every
record of Skyrim, Update, Dawnguard, HearthFires and Dragonborn (1,178,543) read by `CreateFromBinary` and by the
overlay (EditorID read first), compared with `Equals`.
