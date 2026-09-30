# The combined build pinned as 0.54.5-safepatch.5

Six more branches merged into `safepatch-perf-experiment`, against the build pinned as `0.54.5-safepatch.4`
(`7c093038d`):

| Branch | Change |
| --- | --- |
| `fix/root-relative-asset-paths` | Asset paths rooted without a drive (`\meshes\x.nif`) are Data-relative, not an error |
| `perf/mutable-link-cache-lookups` | Mutable link caches look records up in their mod's groups ([issue 229](perf-mutable-link-cache-lookups.md)) |
| `perf/equals-masks` | List and 2D array equals masks built without growing lists; 2D arrays compared by index in Equals |
| `perf/overlay-array2d-bytes` | 2D arrays read from plugins compared by their bytes, decoding only differing elements |
| `perf/differing-fields` | Generated `FillDifferingFields`: which fields of two records differ, without allocating |
| `perf/defer-cell-fill` | Skyrim cells defer their own fields; their child groups are read with them |

## Parity

`check` on Skyrim, Update, Dawnguard, HearthFires and Dragonborn: 1,178,543 records, none differing from the full
parse, none unequal to a second full parse of themselves. `check-threads` on Dawnguard: 2,297,232 concurrent reads,
none differing from a single-threaded read.

## Conflict enumeration

`conflict-scan` on a 760-plugin MO2 profile (2,562,847 versions, 322,306 override pairs), server GC, runs 3 to 5, in
ms. Index includes the bench's own grouping of versions by FormKey (about 200 ms).

| Build, comparer | Reading batches | Index | Compare | Allocated per run |
| --- | ---: | ---: | ---: | ---: |
| safepatch.4, `mask` | | 451-614 | 916-1,827 | 11.3 GB |
| safepatch.4, `equals` | 375-446 | 587-664 | 646-951 | 6.3 GB |
| safepatch.5, `mask` | | 525-546 | 756-2,014 | 7.6 GB |
| safepatch.5, `equals` | 286-501 | 483-669 | 287-482 | 3.5 GB |
| safepatch.5, `differing` | | 574-616 | 710-757 | 4.4 GB |

- `equals` compares in half the time: 2D arrays are no longer boxed and compared by reflection, and arrays read from
  plugins compare by their bytes.
- `differing` answers for every field at about the cost of a mask, allocating 3 GB less: the time is now in reading
  every field of both records (overlay getters build their strings and parts on each read), which `equals` avoids by
  stopping at the first difference.
- Reading batches uses a third less CPU (1.06 s against 1.62 s across threads): cells no longer decompress when read.
  What decompresses now is top-level records still read in full (those with hand-written parsing, such as NPCs).
- The first run of a process is unchanged at 2.2-3.5 s reading and 2-2.7 s comparing.

## Link cache

`linkcache` on vanilla: the mutable cache's 51,782 resolves take 1,183 ms (11,189 ms on safepatch.4); the immutable
cache's take 569 ms.
