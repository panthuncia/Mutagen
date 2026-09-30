# Conflict enumeration on a modded load order

`SafePatch.Bench conflict-scan` on an MO2 profile resolved to its files (760 plugins, 777 MiB, 2,562,847 record
versions, 214,880 overridden records, 322,306 override pairs): index every plugin with `EnumerateMajorRecordBatches`
on every core, group the versions of each FormKey (sharded by FormKey), and compare each override with the version
before it. Server GC. Build: `safepatch-perf-experiment` plus `perf/equals-masks` (`tmp/combined-next`).

## Comparison methods (warm runs)

| Comparer | Compare | Allocated per run |
| --- | ---: | ---: |
| `mask`: generated equals mask | 720-750 ms | 7.6 GB |
| `equals`: generated Equals, stops at the first difference, before 2D arrays compared by index | 710-930 ms | 6.3 GB |
| `equals`, 2D arrays compared by index (`Array2dEquals`) | 280-410 ms | 3.4 GB |

Allocation includes the index. `fields` (Equals restricted to one field at a time) allocated 19 GB and is not a fair
stand-in for a generated per-field comparison.

## Where the time goes (warm run, equals)

Reading batches about 0.29 s, sharding versions by FormKey (the bench's, not Mutagen's) about 0.36 s, comparing about
0.35 s. The comparison is mostly landscapes: completing their deferred fill (decompression) and comparing their arrays.

## Tried and rejected

- **Reusing a decompressor** (SharpZipLib's resettable Inflater, as .NET 10 has no resettable zlib decoder): slower than
  a new ZLibStream per record at every size, from 2.5 against 2.0 us under 512 bytes to 111 against 42 us over 16 KB.
- **Lock-free deferred fill**: no change; `Monitor.LockContentionCount` shows no contention (the profiler's
  `Monitor.Enter_Slowpath` samples are GC suspension).
- **Reading each plugin whole into memory** before opening it: no change to the first run.

## First run

The first run of a process indexes in about 3 s and compares in 1.5-2 s. ReadyToRun (publishing the app with
Mutagen precompiled) saves about 1.2 s per process. The rest of the first index is garbage collection of the groups
it keeps, placed references parsed with their cells, cell decompression, and building each group's record index once.
