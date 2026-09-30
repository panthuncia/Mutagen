# SafePatch.Bench

Benchmarks and parity checks behind a set of Mutagen changes, made while building SafePatch's load-order index. Each
change is a branch off the 0.54.4 tag that can be reviewed and merged on its own:

| Branch | Kind | Summary |
| --- | --- | --- |
| `fix/unsigned-two-byte-enums` | Bug | Two-byte enums were sign-extended by `CreateFromBinary`. |
| `fix/enum-parsevalue-result` | Bug | `EnumBinaryTranslation.ParseValue(reader)` always returned `default`. |
| `fix/nullable-enum-fallback-parse` | Bug | A nullable enum's `-1` fallback was not read back as null by `CreateFromBinary`. |
| `fix/overlay-declared-defaults` | Bug | Overlay fields past a break read as `default` instead of the declared default. |
| `fix/cumulative-break-flags` | Bug | `CreateFromBinary` set only the break it stopped at, not the later ones. |
| `fix/gendered-item-equality` | Bug | Records with gendered fields were unequal to themselves. |
| `fix/condition-pack-data-flag` | Bug | The overlay kept a condition's pack-data bit in `Flags`. |
| `fix/content-equality` | Bug | Lists of byte arrays compared by reference in `Equals`; lists, dictionaries, 2D arrays and byte arrays hashed by reference. |
| `perf/cache-overlay-groups` | Performance | A mod overlay's groups are built once, not on every access. |
| `perf/deferred-overlay-fill` | Performance | Overlay records parse on first read of a field; EditorIDs are read without it. |
| `perf/record-batches` | Performance | `EnumerateMajorRecordBatches` splits one mod for reading on several threads. |

`safepatch-perf-experiment` merges them all (and this folder). `results/` has one file per branch with its
measurements, examples and tests; start at [results/README.md](results/README.md).

## Running

```sh
dotnet build SafePatch.Bench -c Release -p:DisableGitVersionTask=true -p:GeneratePackageOnBuild=false [-p:SafePatchBatches=true]
dotnet SafePatch.Bench/bin/Release/net10.0/SafePatch.Bench.dll <Data folder> <plugins.txt> [runs] [only|-] [label]
dotnet SafePatch.Bench/bin/Release/net10.0/SafePatch.Bench.dll check <Data folder> Skyrim.esm,Update.esm [label]
dotnet SafePatch.Bench/bin/Release/net10.0/SafePatch.Bench.dll check-threads <Data folder> Dawnguard.esm
dotnet SafePatch.Bench/bin/Release/net10.0/SafePatch.Bench.dll check-hash <Data folder> Skyrim.esm,Update.esm [label]
dotnet SafePatch.Bench/bin/Release/net10.0/SafePatch.Bench.dll diag|diag-self <Data folder> Skyrim.esm <record type> [max]
```

The bench compiles SafePatch's scanner from a SafePatch checkout beside this repository (`-p:SafePatchRepo=...`
otherwise). `-p:SafePatchBatches=true` adds the `EnumerateMajorRecordBatches` workload, on trees that have it.
`check` compares every record read by `CreateFromBinary` and by the overlay, and a second `CreateFromBinary`. `check-hash`
counts records equal to the full parse (a second one, or the overlay) whose hash code differs. `diag` prints, for
the records of one type the two reads disagree on, the properties to blame; `diag-self` does the same for two full
parses.

`scripts/` holds what produced `results/`, with this machine's paths: `build-matrix.sh` builds the bench against
each branch, `run-bench.sh` runs the parity and thread checks and `run-timing.sh` the interleaved timings, and
`write_results.py` writes `results/`. `combine.sh` rebuilds `safepatch-perf-experiment`.

## Generated code

Changes to the generator are applied by running `Mutagen.Bethesda.Generator.All` (from its `bin` folder). At 0.54.4
the generator already changes 15 files (Cell and Worldspace trigger ordering, and each game's multi-mod overlay), so
each branch's generated changes were made by regenerating on top of a commit of that drift and cherry-picking the
difference onto 0.54.4. The drift itself is in no branch.
