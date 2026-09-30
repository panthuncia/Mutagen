# Mutable link caches look records up in their mod's groups (issue 229)

Branch `perf/mutable-link-cache-lookups` (one commit on 0.54.4).

`MutableModLinkCache` found a record by enumerating every record of the type asked for and comparing FormKeys. A
load order's mutable cache (what a patcher resolves links through, with the patch mod on top) asks each mutable mod
first, so every resolve scanned the patch, and grew slower as the patch grew. A record of a type that lives only in
a top-level group, or of an interface whose types all do, is now looked up in the mod's groups, which are keyed by
FormKey and always current. Nested records and untyped queries still enumerate.

## Measurements

`SafePatch.Bench linkcache` on vanilla Skyrim SE (82 plugins): for each of the 7,114 winning NPCs, override it into a
patch mod, then resolve the override itself, its template (`INpcSpawnGetter`), race, class, voice, outfit, keywords
and inventory items (`IItemGetter`): 51,782 resolves, all found. The control is the same resolves through the load
order's immutable cache.

| | Total | Per 1,000 NPCs, in order |
| --- | ---: | --- |
| Immutable cache (control) | 583 ms | 432, 14, 111, 13, 3, 4, 4 |
| Mutable cache, 0.54.4 | 11,198 ms | 1,522, 1,058, 1,046, 1,349, 1,792, 2,353, 1,937 |
| Mutable cache, this branch | 1,161 ms | 922, 46, 40, 43, 26, 29, 51 |

The mutable cache no longer slows as the patch grows. What remains is the wrapped immutable cache filling its own
per-type caches (a profile puts `MutableModLinkCache` at about 1% of the mutable path).

## Behaviour

Enumerating a mod by a record subtype's interface yields every record of the base type (`IGlobalIntGetter` yields
float globals too), so the enumeration could return a record of another type. The lookup returns only records of the
type asked for. `MutableLinkCacheLookupTests` checks that the cache finds exactly what enumeration finds (of the type
asked for) for every record type, setter type and link interface of a varied mod, that it sees records added and
removed after it was made, and that contexts still resolve. Mutagen's unit tests pass.
