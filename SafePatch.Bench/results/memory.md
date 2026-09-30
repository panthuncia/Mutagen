# Memory: 0.54.4 against the combined build

`SafePatch.Bench memory` runs one scenario per process over the vanilla load order (82 plugins, 364 MiB,
1,252,863 records), on 0.54.4 (`base-4`) and the combined build pinned as `0.54.5-safepatch.4` (`combined-4`,
`7c093038d`). Three processes per scenario and build, alternating builds (`scripts/run-memory.sh`, log in
`logs/memory.log`); medians, in MiB. Runs varied by under 1% for the heap and 2% for peaks.

- **Retained:** the managed heap the scenario keeps alive, after a full collection, against the heap before it.
- **Peak working set** and **peak private:** the process's peaks (`PeakWorkingSet64`, `PeakPagedMemorySize64`),
  runtime included.
- **Allocated:** every byte allocated during the scenario, garbage included.

| Scenario | Retained | Peak working set | Peak private | Allocated | Time (ms) |
| --- | ---: | ---: | ---: | ---: | ---: |
| `open`: plugins open, every record's FormKey read, nothing kept | 4 → 376 | 468 → 543 | 381 → 458 | 2,358 → 1,402 | 2,804 → 2,466 |
| `keep`: every record object kept | 1,598 → 1,126 | 1,881 → 1,438 | 1,799 → 1,360 | 2,393 → 1,428 | 5,038 → 4,093 |
| `keep-read`: every record kept, every field read | 1,684 → 1,713 | 2,041 → 2,113 | 1,950 → 2,024 | 6,189 → 5,851 | 7,075 → 6,706 |
| `full`: `CreateFromBinary` of every plugin, kept | 3,004 → 3,004 | 3,701 → 3,687 | 3,622 → 3,612 | 5,994 → 5,995 | 9,303 → 9,094 |

`keep-read` reads every public property of every record's overlay and enumerates every list property (142,002,542
values on both builds); properties holding other major records are left out, since those records are read in their
own right. One property throws on both builds (`Race.ExportingExtraNam3`, not implemented) and is skipped.

## What changes

- **Plugins open, records not kept: +372 MiB retained.** A group overlay copies its bytes out of the file when built
  (`PluginBinaryOverlay.LockExtractMemory`). On 0.54.4 that copy is garbage once the group has been read, unless
  records from it are kept. `perf/cache-overlay-groups` keeps each group for the mod's lifetime, and with it the copy
  of the group's bytes and its record index: once every group has been read, about the load order's size stays in
  memory for as long as the plugins are open.
- **Records kept, not read: -472 MiB retained, -443 MiB peak.** `perf/deferred-overlay-fill` keeps a record's header
  and the state to read it later, not the locations of all its fields. Group bytes are retained on both builds here,
  since kept records point into them.
- **Every field read: +29 MiB retained (+1.7%), +72 MiB peak.** Once filled, a deferred record keeps the same field
  locations as an eager one, and every overlay object has one more reference field (the deferred state, null once
  filled).
- **Full parse: unchanged**, as it does not use overlays.
- **Allocation** is lower in every overlay scenario (by 956 MiB for `open`), since records whose fields are never
  read are never parsed.
