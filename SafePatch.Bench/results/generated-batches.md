# The combined build with generated batches, against 0.54.4

`perf/record-batches` now generates `EnumerateMajorRecordBatches` for every game (`MajorRecordBatchesModule`) in
place of the hand-written Skyrim version, and gives each record the record it is nested in (`MajorRecordWithParent`).
The split is the same: each top-level group, each worldspace with its persistent cell, and each block of interior or
exterior cells. Only 0.54.4 (`base-3`) and the combined build (`combined-3`, `safepatch-perf-experiment` with the
generated batches) were timed, in the same session, alternating round by round
(`scripts/run-timing-generated-batches.sh`, log in `logs/timing-generated-batches.log`).

| Workload | base-3 | combined-3 | Change |
| --- | ---: | ---: | ---: |
| open, then every record's FormKey and EditorID | 1,099 (1,049) | 1,001 (393) | -9% |
| every record's FormKey | 1,036 (1,027) | 216 (198) | -79% |
| every record's FormKey and EditorID | 1,044 (1,037) | 302 (293) | -71% |
| keep every record object | 2,967 (2,785) | 1,278 (1,217) | -57% |
| every record's FormKey and EditorID, within plugins in parallel | 296 (280) | 95 (86) | -68% |
| open, then the same within plugins in parallel | 307 (283) | 162 (151) | -47% |
| the same through EnumerateMajorRecordBatches (with parents) |  | 94 (90) |  |
| 2,000 statics by key | 945 (923) | 1 (1) | -100% |
| full parse (CreateFromBinary) of every plugin | 6,090 (5,590) | 6,219 (5,599) | +2% |
| Scanner: every record's FormKey and EditorID | 129 (100) | 132 (118) | +2% |
| Heap holding every record object | 1,578 MiB | 1,106 MiB | -30% |

Every workload read the same 1,252,863 records, and within each run the batches' checksum of FormKeys and EditorIDs
equals the other workloads'. The generated batches, which also yield each record's parent, take 94 ms against 95 ms for
the scanner-shaped split written in the bench, and 95 ms for the hand-written version before (`combined.md`). The
first "open, then" run of each round is slow on both builds (the files are read cold), so that row's median is mostly
cold reads; its best run shows the difference.

Measured on the setup in `README.md` (Skyrim SE 1.6.1170, 82 plugins, 364 MiB; Ryzen 7 9800X3D; .NET 10.0.301):
three rounds, three runs of each workload per round, median of the rounds' medians, best run in brackets, in ms.
