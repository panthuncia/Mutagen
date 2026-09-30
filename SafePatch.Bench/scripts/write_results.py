"""Writes SafePatch.Bench/results/*.md from bench-results2.log: one file per branch, and an index."""
import os, statistics, sys
from collections import defaultdict

LOG = r'C:\Users\matth\source\repos\Mutagen-wt\bench-results2.log'
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

rounds = defaultdict(lambda: defaultdict(list))   # step -> variant -> [(median, best)]
records = {}
parity = defaultdict(dict)
selfeq = defaultdict(dict)
hashes = defaultdict(dict)   # variant -> type -> equal records whose hash code differs
threads = {}
current = None
def lines():
    # The combined build has been rebuilt since the first session (a stale build, then two more fix branches): its
    # lines there are replaced by later runs. The later fix branches and the rebuilt combined build were timed in a
    # session of their own, alongside 0.54.4 (base-2) and the combined build (combined-2).
    for line in open(LOG, encoding='utf-8', errors='replace'):
        if '|combined|' in line or line.startswith('== threads combined'): continue
        yield line
    for extra in ('parity-new.log', 'parity-combined3.log', 'parity-all-base.log', 'parity-all-combined.log',
                  'parity-content.log', 'parity-epsilon.log', 'hash-masters.log', 'hash-all.log', 'timing-new.log', 'threads-combined.log'):
        for line in open(os.path.join(os.path.dirname(LOG), extra), encoding='utf-8', errors='replace'):
            yield line
for line in lines():
    line = line.rstrip('\n')
    if line.startswith('== threads '):
        current = line.split()[2]
    elif 'concurrent reads' in line and current:
        threads[current] = line.strip()
    parts = line.split('|')
    if parts[0] == 'RESULT' and len(parts) == 7:
        _, v, step, med, best, recs, chk = parts
        rounds[step][v].append((int(med), int(best)))
        records[(step, v)] = (int(recs), int(chk))
    elif parts[0] == 'PARITY' and len(parts) == 4:
        parity[parts[1]][parts[2]] = int(parts[3])
    elif parts[0] == 'SELF' and len(parts) == 4:
        selfeq[parts[1]][parts[2]] = int(parts[3])
    elif parts[0] == 'HASH' and len(parts) == 4:
        hashes[parts[1]][parts[2]] = int(parts[3])

def timing(step, v):
    runs = rounds[step].get(v)
    if not runs: return None
    return statistics.median(m for m, _ in runs), min(b for _, b in runs)

def cell(step, v):
    t = timing(step, v)
    return '' if t is None else f'{t[0]:,.0f} ({t[1]:,})'

def change(step, v, base='base'):
    a, b = timing(step, base), timing(step, v)
    if not a or not b: return ''
    return f'{(b[0] - a[0]) / a[0] * 100:+.0f}%'

def heap(v):
    r = records.get(('Mutagen: keep every record object (heap MiB as Checksum)', v))
    return f'{r[1]:,} MiB' if r else ''

STEPS = [s for s in rounds]
PERF_STEPS = [
    'Mutagen: open, then every record\'s FormKey and EditorID',
    'Mutagen: every record\'s FormKey',
    'Mutagen: every record\'s FormKey and EditorID',
    'Mutagen: keep every record object (heap MiB as Checksum)',
    'Mutagen: every record\'s FormKey and EditorID, within plugins in parallel',
    'Mutagen: open, then the same within plugins in parallel',
    'Mutagen: the same through EnumerateMajorRecordBatches',
    'Mutagen: 2,000 statics by key',
    'Mutagen: full parse (CreateFromBinary) of every plugin',
    'Scanner: every record\'s FormKey and EditorID',
]

def timing_table(variants, steps=None, base='base'):
    steps = [s for s in (steps or PERF_STEPS) if s in rounds]
    rows = ['| Workload | ' + ' | '.join(variants) + ' | Change |', '| --- |' + ' ---: |' * (len(variants) + 1)]
    for s in steps:
        if all(timing(s, v) is None for v in variants): continue
        rows.append(f'| {s.replace("Mutagen: ", "").replace(" (heap MiB as Checksum)", "")} | ' + ' | '.join(cell(s, v) for v in variants) + f' | {change(s, variants[-1], variants[0])} |')
    rows.append('| Heap holding every record object | ' + ' | '.join(heap(v) for v in variants) + ' | |')
    return '\n'.join(rows)

def parity_table(variants):
    types = sorted({t for v in variants for t in parity.get(v, {}) if not t.startswith('(')})
    rows = ['| Record type | ' + ' | '.join(variants) + ' |', '| --- |' + ' ---: |' * len(variants)]
    for t in types:
        counts = [parity.get(v, {}).get(t, 0) for v in variants]
        if len(set(counts)) == 1 and len(variants) > 1 and 'combined' not in variants: continue
        rows.append(f'| {t} | ' + ' | '.join(f'{c:,}' for c in counts) + ' |')
    stypes = sorted({t for v in variants for t in selfeq.get(v, {})})
    self_rows = ['| Record type | ' + ' | '.join(variants) + ' |', '| --- |' + ' ---: |' * len(variants)]
    for t in stypes:
        counts = [selfeq.get(v, {}).get(t, 0) for v in variants]
        if len(set(counts)) == 1 and 'combined' not in variants: continue
        self_rows.append(f'| {t} | ' + ' | '.join(f'{c:,}' for c in counts) + ' |')
    table = '\n'.join(rows) if len(rows) > 2 else 'none; the same records differ, by the same counts, as on 0.54.4.'
    return table, '\n'.join(self_rows) if len(self_rows) > 2 else None

def hash_table(variants, top=10):
    types = sorted({t for v in variants for t in hashes.get(v, {}) if not t.startswith('(')}, key=lambda t: -hashes[variants[0]].get(t, 0))
    rows = ['| Record type | ' + ' | '.join(variants) + ' |', '| --- |' + ' ---: |' * len(variants)]
    for t in types[:top]:
        rows.append(f'| {t} | ' + ' | '.join(f'{hashes.get(v, {}).get(t, 0):,}' for v in variants) + ' |')
    if len(types) > top:
        rest = types[top:]
        rows.append(f'| {len(rest)} other types | ' + ' | '.join(f'{sum(hashes.get(v, {}).get(t, 0) for t in rest):,}' for v in variants) + ' |')
    rows.append('| **All types** | ' + ' | '.join(f"**{sum(c for t, c in hashes.get(v, {}).items() if not t.startswith('(')):,}**" for v in variants) + ' |')
    rows.append('| (equal pairs checked) | ' + ' | '.join(f"{hashes.get(v, {}).get('(equal pairs)', 0):,}" for v in variants) + ' |')
    return chr(10).join(rows)

SETUP = """Measured on Skyrim SE 1.6.1170 with its base game and Creation Club plugins (82 plugins, 364 MiB,
1,252,863 records), on a Ryzen 7 9800X3D (8 cores, 16 threads), 126 GB RAM, NVMe SSD, Windows 11, .NET 10.0.301.
Each branch is built from 0.54.4 on its own, with the same `SafePatch.Bench`. Timings: three rounds, each running
every build once (so a busy spell on the machine is shared out rather than landing on one build), three runs of each
workload per round; the table gives the median of the rounds' medians, and the best run in brackets, in ms. Runs
of the same code vary by about 10%, more on a busy machine, so changes smaller than that are noise. Parity: every
record of Skyrim, Update, Dawnguard, HearthFires and Dragonborn (1,178,543) read by `CreateFromBinary` and by the
overlay (EditorID read first), compared with `Equals`."""

def write(name, text):
    open(os.path.join(OUT, name), 'w', encoding='utf-8', newline='\n').write(text.strip() + '\n')

BRANCHES = {}

def fix(file, branch, variant, title, what, tests, parity_note, extra='', base='base', commits='one commit'):
    pt, st = parity_table(['base', variant])
    body = f"""# {title}

Branch `{branch}` ({commits} on 0.54.4).

{what}

## Effect on reads

Records that `CreateFromBinary` and the overlay read differently, before and after:

{pt}
"""
    if st:
        body += f"""
Records unequal to a second `CreateFromBinary` of the same bytes:

{st}
"""
    body += f"""
{parity_note}

## Cost

Reading is no slower. Full parse and overlay timings (unchanged code paths vary by about 10%):

{timing_table([base, variant], ['Mutagen: full parse (CreateFromBinary) of every plugin', "Mutagen: every record's FormKey and EditorID", 'Mutagen: keep every record object (heap MiB as Checksum)'])}

## Tests

{tests}

{extra}

## Setup

{SETUP}
"""
    write(file, body)
    BRANCHES[branch] = (file, title)

def run():
    fix('fix-unsigned-two-byte-enums.md', 'fix/unsigned-two-byte-enums', 'fix-unsigned-enums',
        'Read two-byte enums unsigned',
        """`EnumBinaryTranslation` read a two-byte enum as a signed short, so a value with the high bit set was
sign-extended. The overlay reads the same bytes unsigned. Example: `ThievesGuildSapphireSandboxMultiPackage0x0`
(`000F8D:Skyrim.esm`) has `InterruptFlags` bytes `B7 FC`; `CreateFromBinary` gave `-841`, the overlay `64695`. A
quest alias's `CreateReferenceToObject.Create` of `0x8000` (`In`) came back as `-32768`, which is no member of the
enum. Both write the same bytes; they differ in memory, so equality and flag tests disagree between the two reads.
No two-byte enum in any game defines a negative member (62 checked), so reading unsigned changes nothing else.""",
        '`EnumBinaryTranslationTests`: `0x8001` read through both paths is `High | Low` (fails before the fix).',
        'The remaining Package and Quest differences are other issues (below and in the other branches).')
    write('fix-enum-parsevalue-result.md', f"""# Return the parsed value from `EnumBinaryTranslation.ParseValue(reader)`

Branch `fix/enum-parsevalue-result` (one commit on 0.54.4).

`ParseValue(TReader)` read and converted the value and then returned `default(TEnum)` for every input. Nothing in
Mutagen's record code calls it (only a benchmark in `Mutagen.Bethesda.Tests`), so reading plugins is unaffected
and there is no performance effect to measure; the fix is for callers of the public API. It now reads through the
same path as `Parse(reader, length)`.

## Tests

`EnumBinaryTranslationParseValueTests`: one-, two- and four-byte values read back as `Value.Two` (all fail before
the fix, returning `None`).

It conflicts textually with `fix/unsigned-two-byte-enums` (both touch the two-byte case); merged, `ParseValue`
delegates to the unsigned read.
""")
    BRANCHES['fix/enum-parsevalue-result'] = ('fix-enum-parsevalue-result.md', 'Return the parsed value from EnumBinaryTranslation.ParseValue(reader)')
    fix('fix-nullable-enum-fallback-parse.md', 'fix/nullable-enum-fallback-parse', 'fix-nullable-fallback',
        'Read a nullable enum\'s binary fallback back as null',
        """Enums declared with `nullableBinaryFallback` (Skyrim's `WeaponData.Skill`, `BookSkill`, `Class` and
`WorkbenchData` skills, and their Oblivion and Fallout 3 equivalents; 13 fields) are written as the fallback, `-1`,
when null, and the overlay reads `-1` back as null. The full parse kept `(Skill)(-1)`. Example:
`dunMarkarthWizardSpiderControlStaffFake` (`0C19FF:Skyrim.esm`): `Data.Skill` is `-1` from `CreateFromBinary`,
null from the overlay. The generated copy-in now maps the fallback to null.""",
        '`NullableEnumFallbackTests`: a weapon written with a null skill reads back null through both paths (the full parse fails before the fix).',
        '')
    fix('fix-overlay-declared-defaults.md', 'fix/overlay-declared-defaults', 'fix-overlay-defaults',
        'Give overlay fields past a break the default a full parse gives them',
        """When a record's data stops at a break, the fields after it are not in the file. `CreateFromBinary` leaves
them at their defaults, as a new object has them; the overlay returned `default(T)` or an empty array. Examples:
`LoadScreenMRaltar01` (`000E87:Skyrim.esm`) has an 8-byte `DNAM` that stops after the material, and its `Unused` is
`[0, 0, 0]` from the full parse and `[]` from the overlay (9,720 statics in Skyrim.esm alone); `DA08EbonyBladeTracking`
(`10FAEC:Skyrim.esm`) has script data without fragments, and its `ExtraBindDataVersion` is 2 (the declared default)
against 0. Overlay getters now return the field's declared default, and zeros of the field's length for fixed-length
byte arrays, as the overlay's struct path already did. 38 generated files change across the games.""",
        '`OverlayDefaultsTests`: a static stopping at the break has `Unused` of three zero bytes, and a quest adapter without fragments has `ExtraBindDataVersion` 2, through both paths (the overlay fails before the fix).',
        'The remaining Quest differences are two-byte enums (see `fix/unsigned-two-byte-enums`).')
    fix('fix-cumulative-break-flags.md', 'fix/cumulative-break-flags', 'fix-cumulative-breaks',
        'Set every later break flag when a full parse stops at a break',
        """Break flags say that a record's data stops before a break, so the fields after it and after every later
break are absent. The overlay tests each break against the data's length and sets them all; `CreateFromBinary` set
only the break it stopped at. Example: `DefaultCombatstyle` (`00003D:Skyrim.esm`): `Flight.Versioning` is `Break2`
from the full parse and `Break2, Break3` from the overlay; `FXWorkingExampleProjectile` (`020E20:Skyrim.esm`):
`DATADataTypeState` is `Break0` against `Break0, Break1`. The same bytes are written either way, but a caller
asking `HasFlag(Break3)` of the full parse was told the fields after `Break3` were there. 60 generated lines change.""",
        '`BreakFlagsTests`: crime values stopping at the first break read as `Break0 | Break1` through both paths (the full parse fails before the fix).',
        'The remaining Faction differences are gendered rank titles (see `fix/gendered-item-equality`).')
    fix('fix-gendered-item-equality.md', 'fix/gendered-item-equality', 'fix-gendered-equality',
        'Compare gendered items by value',
        """`GenderedItem<T>`, the overlay's `GenderedItemBinaryOverlay<T>` and Skyrim's
`ArmorAddonWeightSliderContainer` did not override `Equals`, and generated record `Equals` compares gendered fields
with `object.Equals`. So every record with a gendered field (armor, armor addons, races, factions' rank titles,
association types) was unequal to every other instance of itself, including a second `CreateFromBinary` of the same
bytes, while `GetEqualsMask` said they were equal. They now compare their male and female items, with a matching hash
code, and the comparison holds across implementations whose declared item types differ (an object's
`GenderedItem<ArmorModel?>` against an overlay's `IGenderedItemGetter<IArmorModelGetter?>`).""",
        '`GenderedItemEqualityTests`: two gendered items with the same values are equal with equal hashes; an armor addon equals a second full parse and its overlay; an armor with gendered models equals its overlay both ways round (all fail before the fix).',
        'This fix also exposed a bug in the deferred fill (gendered fields read before the fill), fixed on `perf/deferred-overlay-fill`.')
    later = 'Timed in a later session than the branches above, alongside 0.54.4 again (`base-2`).'
    fix('fix-condition-pack-data-flag.md', 'fix/condition-pack-data-flag', 'fix-condition-pack-data',
        'Keep a condition\'s pack-data bit out of Flags in the overlay',
        """In Skyrim and Starfield, two of a condition's flag bits, "use aliases" (`0x02`) and "use pack data" (`0x08`),
are exposed on its data (`UseAliases`, `UsePackageData`), not in `Condition.Flags`, whose enum does not define them.
`CreateFromBinary` clears both from `Flags`; the overlay cleared only the aliases bit. Example: `WERJ02PlayerThief`
(`0B8152:Skyrim.esm`): its first response's fifth condition has `Flags` `OR` from the full parse and `9` (`OR` and
the undefined `0x08`) from the overlay; `HoldPositionWithTravel1024` (`10FAAF:Skyrim.esm`): a procedure's condition
has `0` against `8`. Besides breaking equality, a condition copied from the overlay kept the bit in `Flags`, so the
writer, which ORs `UsePackageData` into `Flags`, wrote it even after `UsePackageData` was turned off. The overlay now
clears the bit as the full parse does. Fallout 4 keeps these bits in its `Flag` enum and is unaffected.""",
        '`ConditionPackDataFlagTests` (Skyrim and Starfield): a condition written with `UsePackageData` reads back with `Flags` of `OR` alone and `UsePackageData` set, through both paths (the overlay fails before the fix).',
        """Most packages with such conditions also differ by two-byte enums (see `fix/unsigned-two-byte-enums`), so the
package count falls by only 10 here. Of the 59 packages that fix leaves, 53 have such conditions and the other 6
differ only by the lists of `fix/content-equality`; with every branch merged, no package differs (`combined.md`).""",
        later, base='base-2')
    fix('fix-content-equality.md', 'fix/content-equality', 'fix-content-equality',
        'Compare and hash lists and byte arrays by content',
        f"""Two commits, one for each generated member:

1. **`Equals` on lists of byte arrays.** A list of byte arrays holds memory slices, and the generated `Equals`
   compared the lists with `SequenceEqualNullable`, which uses the slice's own `Equals`: where it points, not its
   bytes. So a record with a non-empty list of byte arrays was unequal to every other instance of itself, including
   a second `CreateFromBinary` of the same bytes. Seven fields have this shape: Skyrim's `MaterialObject.DNAMs`,
   `DialogView.TNAMs` and `PackageBranch.Unknown`, the same three in Fallout 4, and Starfield's
   `NavigationMeshObstacleManagerSubObject` field. Example: `SnowMaterialGlacier` (`05E3F0:Skyrim.esm`) and
   `MQ202ViewShared` (`06C866:Skyrim.esm`) are unequal to themselves read twice, with every property equal by value.
   The equals mask already compared the elements by content (which is why `GetEqualsMask` reported no difference);
   `Equals` now does the same.
2. **`GetHashCode` on lists, dictionaries, 2D arrays and byte arrays.** The generated hash added these fields as
   objects, whose hash codes are the object's, while `Equals` compares their contents. So records equal by `Equals`
   hashed differently whenever they held any of these, which is most record types, and so did their parts: across
   two reads of Skyrim.esm, 985,151 of 1,262,207 equal list elements (faction ranks, perks, effects, quest aliases,
   cell references) had different hash codes. Anything that keys a dictionary or set by records or their parts found
   equal ones missing. The hash now adds their contents (`ContentHashExt.AddContents`; order-independent for
   dictionaries). 894 generated files change, each only in `GetHashCode`.

The generator gains `MutagenListType`, `MutagenArrayType`, `MutagenArray2dType` and `MutagenByteArrayType`, which
Mutagen now uses in place of Loqui's (as it already did for `Dict`), overriding only these members. The plugin
translation module registers its generators for them, and the mask module its 2D-array one.

Records equal to the full parse (a second full parse, and the overlay) whose hash code differs, in the five masters:

{hash_table(['base', 'fix-content-equality'])}

(Records unequal to their twin, such as those with gendered fields before `fix/gendered-item-equality`, are not
counted: the hash code is only required to agree for equal records. See `combined.md` for all branches together.)""",
        '`ByteArrayListEqualityTests`: two material objects with equal `DNAMs` contents are equal, and unequal once one byte differs; a material object written and read equals a second full parse and its overlay (all fail before the fix). `ContentHashCodeTests`: an NPC with factions and keywords, a spell with a conditioned effect and a material object each hash alike across two full parses and the overlay; list contents hash in order, dictionaries in any order.',
        """3 material objects still differ between the reads here: their break flags, fixed by
`fix/cumulative-break-flags` (where the list hid them).""",
        later, base='base-2', commits='two commits')

    fix('fix-overlay-float-epsilon.md', 'fix/overlay-float-epsilon', 'fix-overlay-float-epsilon',
        'Read float.Epsilon as zero in overlays, as the full parse does',
        f"""Files store some zeros as `float.Epsilon` (1E-45, the bits `01 00 00 00`). `CreateFromBinary` reads it as
zero and the writer writes zero for it, but overlays read the raw value. `Equals` compares floats within a tolerance
and does not notice; `==` and `GetHashCode` do. Example: `FireStormExplosion02` (`0877F9:Skyrim.esm`) and three
other explosions have `ISRadius` 0 from the full parse and 1E-45 from the overlay. It showed once
`fix/content-equality` made hash codes comparable, as the only records in the merged branches that were equal but
hashed differently.

Overlay float getters, generated (564 files) and hand-written (condition comparison values, navmesh data), now read
through `FloatBinaryTranslation.GetFloat`, which applies the full parse's rule, and so do `P2Float`'s and `P3Float`'s
span reads. Integer-backed floats already did. Placed primitives' and worldspaces' bounds read the raw value in both
paths, and stay so.

Records equal to the full parse whose hash code differs, with `fix/content-equality` and without:

{hash_table(['base', 'fix-content-equality', 'fix-overlay-float-epsilon', 'combined'])}

On this branch alone, lists and byte arrays still hash by reference (`fix/content-equality`), and the four
explosions are not counted: until `fix/cumulative-break-flags` their break flags make them unequal to the overlay. With
every branch merged (`combined`), no equal records hash differently; without this fix, those four did.""",
        '`FloatEpsilonTests`: an explosion radius and a condition comparison value stored as `float.Epsilon` read as zero through both paths, and the explosion hashes alike (fails before the fix).',
        'The parity check compares with `Equals`, whose tolerance hides this difference, so its counts do not change.',
        later, base='base-2')

    # Performance branches.
    write('perf-cache-overlay-groups.md', f"""# Build each group of a mod overlay once

Branch `perf/cache-overlay-groups` (one commit on 0.54.4).

A mod overlay's group getters (`mod.Statics`, `mod.Npcs`, ...) built a new group overlay on every access, locating
the group's records again. Code that reaches a group per record (`TryGetValue` in a loop, or a link cache resolving
through it) paid that every time. Each group overlay is now built on first access and kept for the mod's lifetime.

## Measurements

{timing_table(['base', 'perf-group-cache'])}

Looking up 2,000 statics by FormKey through `mod.Statics.TryGetValue` goes from about a second to about a
millisecond; everything else is unchanged within noise. The cost is the group overlays kept in memory once touched
(the heap row: every group touched once).

## Parity and tests

Reads are unchanged: the parity check matches 0.54.4 exactly on all 1,178,543 records. Mutagen's unit tests pass
(3,308 and 789). The change is in the generator (`LoquiBinaryTranslationGeneration`) and every game's mod overlay.

## Setup

{SETUP}
""")
    BRANCHES['perf/cache-overlay-groups'] = ('perf-cache-overlay-groups.md', 'Build each group of a mod overlay once')

    pt, st = parity_table(['base', 'perf-deferred-fill', 'perf-deferred-fill-peek', 'perf-deferred-fill-placed'])
    write('perf-deferred-overlay-fill.md', f"""# Defer an overlay record's fill until a field is read

Branch `perf/deferred-overlay-fill`, three commits on 0.54.4, measured one by one:

1. `Defer an overlay record's fill until a field is read` (`perf-deferred-fill`): a major record's overlay is created
   from its header and runs decompression and the subrecord walk on the first read of anything else, once and
   thread-safely. The generator routes every member the fill sets through `LazyFill`.
2. `Read a deferred record's EditorID without filling it` (`perf-deferred-fill-peek`): the EditorID comes from the
   first subrecord, inflating a compressed record only that far (`InflatePrefix`).
3. `Defer Skyrim placed objects' fill` (`perf-deferred-fill-placed`): placed objects' hand-written overlay code audited
   so they can be deferred too.

## Measurements

{timing_table(['base', 'perf-deferred-fill', 'perf-deferred-fill-peek', 'perf-deferred-fill-placed'])}

- The fill alone makes enumerating records for their FormKeys about four times faster and the heap holding every
  record about a third smaller, since records nobody reads are never parsed.
- The EditorID peek is what makes FormKey-and-EditorID enumeration fast: without it, reading the EditorID completes
  the fill.
- **Deferring placed objects is not justified by these numbers.** Against the commit before it, its best runs are
  the same (the open-and-read workload's medians swing by hundreds of milliseconds between rounds, so its median gain
  is not to be trusted), and it holds 65 MiB more: a deferred-state object for each of the 916,540 placed objects,
  which were cheap to fill eagerly. The third commit can be left out of a pull request.

## Parity

Records `CreateFromBinary` and the overlay read differently, against 0.54.4: {pt} Concurrent reads (eight
threads racing to complete each fresh record's fill): {threads.get('perf-deferred-fill', '').split(': ')[-1]}

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

{SETUP}
""")
    BRANCHES['perf/deferred-overlay-fill'] = ('perf-deferred-overlay-fill.md', "Defer an overlay record's fill until a field is read")

    write('perf-record-batches.md', f"""# `EnumerateMajorRecordBatches`: one mod on several threads

Branch `perf/record-batches` (one commit on 0.54.4).

`EnumerateMajorRecords` yields a mod's records one after another, so a caller can parallelise only across mods, and
Skyrim.esm (70% of a vanilla load order's records) runs on one core. The new extension yields the same records in
batches that can be read on different threads: each top-level group, the worldspaces with their persistent cells,
and each block of interior or exterior cells. It is hand-written for Skyrim.

## Measurements

On 0.54.4 alone (each plugin on its own thread, against each plugin split into batches):

{timing_table(['base', 'perf-record-batches'], ["Mutagen: every record's FormKey and EditorID", "Mutagen: every record's FormKey and EditorID, within plugins in parallel", 'Mutagen: the same through EnumerateMajorRecordBatches'])}

The gain grows with the other changes: with deferred fills, per-record work is small enough that the single-core
plugin dominates (see `combined.md`).

## Parity and tests

Reads are unchanged. `RecordBatchesTests`: the batches hold exactly the records `EnumerateMajorRecords` yields, for a
mod with interior cells, a worldspace with persistent and exterior cells, and a topic with responses. Mutagen's unit
tests pass.

## Setup

{SETUP}
""")
    BRANCHES['perf/record-batches'] = ('perf-record-batches.md', 'EnumerateMajorRecordBatches: one mod on several threads')

    pt, st = parity_table(['base', 'combined'])
    all_pt, _ = parity_table(['base-all', 'combined-all'])
    write('combined.md', f"""# All branches together (`safepatch-perf-experiment`)

Every fix and performance branch merged onto 0.54.4, the generated code regenerated from the merged generator.

## Measurements

Timed in the same session as the three later fix branches, alternating 0.54.4 (`base-2`) and the combined build
(`combined-2`) round by round:

{timing_table(['base-2', 'combined-2'])}

## Parity

Records of the five masters that `CreateFromBinary` and the overlay read differently:

{pt}

Records unequal to a second `CreateFromBinary` of the same bytes:

{st or 'none'}

The other 77 plugins (Creation Club content and one landscape mod, 74,320 records), read the same way:

{all_pt}

Every record of the load order now reads the same through both paths and equals a second full parse of itself.

Records equal to the full parse (a second full parse, and the overlay) whose hash code differs:

{hash_table(['base', 'combined'])}

and across the other 77 plugins:

{hash_table(['base-all', 'combined-all'])}

Concurrent reads: {threads.get('combined', '').split(': ')[-1]}

## Setup

{SETUP}
""")

    index = ['# SafePatch.Bench results', '', 'One file per branch, each measured on its own against 0.54.4, and the branches combined:', '']
    for branch, (file, title) in BRANCHES.items():
        index.append(f'- [`{branch}`]({file}): {title}')
    index += ['- [All combined](combined.md)', '', '## Setup', '', SETUP, '', 'Reproduce with `build-matrix.sh`, `run-bench.sh` and `run-timing.sh` beside this folder (see `../README.md`).']
    write('README.md', '\n'.join(index))

run()
print('wrote', sorted(os.listdir(OUT)))
