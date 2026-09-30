using System.Collections.Concurrent;
using System.Diagnostics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using SafePatch.Authoring.Index;

// SafePatch experiment: Mutagen read paths against SafePatch's raw scanner.
//   SafePatch.Bench <Data folder> <plugins.txt> [runs=3] [only=<step name part>|-] [label]
//   SafePatch.Bench check <Data folder> <plugin,...> [label]
//   SafePatch.Bench check-threads <Data folder> <plugin>
//   SafePatch.Bench conflicts|conflicts-self <Data folder> <plugins.txt> [runs=3] [label]
//   SafePatch.Bench memory <Data folder> <plugins.txt> open|keep|keep-read|full [label]
//   SafePatch.Bench check-hash <Data folder> <plugin,...> [label]
//   SafePatch.Bench diag|diag-self|diag-hash <Data folder> <plugin> <record type> [max=3]
if (args is ["check", var checkData, var checkPlugins, .. var checkRest])
{
    return Check(checkData, checkPlugins.Split(','), checkRest is [var checkLabel] ? checkLabel : "");
}
if (args is ["diag" or "diag-self" or "diag-hash", var diagData, var diagPlugin, var diagType, .. var diagRest])
{
    return Diag.Run(diagData, diagPlugin, diagType, diagRest is [var n] ? int.Parse(n) : 3, self: args[0] == "diag-self", hash: args[0] == "diag-hash");
}
if (args is ["check-hash", var hashData, var hashPlugins, .. var hashRest])
{
    return CheckHash(hashData, hashPlugins.Split(','), hashRest is [var hashLabel] ? hashLabel : "");
}
if (args is ["conflicts" or "conflicts-self", var conflictData, var conflictPlugins, .. var conflictRest])
{
    return Conflicts.Run(conflictData, conflictPlugins, conflictRest is [var r, ..] ? int.Parse(r) : 3, conflictRest is [_, var conflictLabel] ? conflictLabel : "", self: args[0] == "conflicts-self");
}
if (args is ["memory", var memoryData, var memoryPlugins, var memoryScenario, .. var memoryRest])
{
    return Memory.Run(memoryData, memoryPlugins, memoryScenario, memoryRest is [var memoryLabel] ? memoryLabel : "");
}
if (args is ["check-threads", var threadData, var threadPlugin])
{
    return CheckThreads(threadData, threadPlugin);
}
if (args.Length < 2)
{
    Console.Error.WriteLine("SafePatch.Bench <Data folder> <plugins.txt> [runs] [only]");
    return 2;
}
var data = args[0];
var runs = args.Length > 2 ? int.Parse(args[2]) : 3;
var only = args.Length > 3 && args[3] != "-" ? args[3] : null;
// A label for this build, printed on each result line for collecting results across builds.
var label = args.Length > 4 ? args[4] : "";
var release = GameRelease.SkyrimSE;
var listings = LoadOrder.GetLoadOrderListings(release, args[1], null, data, throwOnMissingMods: false)
    .Where(l => l.Enabled && File.Exists(Path.Combine(data, l.ModKey.FileName))).ToList();
var order = listings.Select(l => (Key: l.ModKey, Path: Path.Combine(data, l.ModKey.FileName))).ToList();
var megabytes = order.Sum(o => new FileInfo(o.Path).Length) / (1024.0 * 1024);
Console.WriteLine($"{order.Count} plugins, {megabytes:F0} MiB; Mutagen built from {typeof(SkyrimMod).Assembly.Location}");

ISkyrimModGetter[] Open() =>
    [.. order.AsParallel().AsOrdered().Select(o => SkyrimMod.CreateFromBinaryOverlay(new ModPath(o.Key, o.Path), SkyrimRelease.SkyrimSE))];

using var warm = new ModSet(Open());
var statics = warm.Mods.SelectMany(m => m.Statics.Select(s => (Mod: m, s.FormKey))).Where((_, i) => i % 7 == 0).Take(2000).ToList();

var steps = new List<(string Name, Func<Result> Run)>
{
    ("Mutagen: open every plugin (overlay)", () => { using var set = new ModSet(Open()); return new Result(set.Mods.Length, 0); }),
    ("Mutagen: open, then every record's FormKey and EditorID", () => { using var set = new ModSet(Open()); return Index(set.Mods, editorIds: true); }),
    ("Mutagen: every record's FormKey", () => Index(warm.Mods, editorIds: false)),
    ("Mutagen: every record's FormKey and EditorID", () => Index(warm.Mods, editorIds: true)),
    ("Mutagen: keep every record object (heap MiB as Checksum)", () =>
    {
        using var set = new ModSet(Open());
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var kept = set.Mods.SelectMany(m => m.EnumerateMajorRecords()).ToList();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(kept);
        return new Result(kept.Count, (after - before) / (1024 * 1024));
    }),
    ("Mutagen: every record's FormKey and EditorID, within plugins in parallel", () => IndexWithin(warm.Mods, editorIds: true)),
    ("Mutagen: open, then the same within plugins in parallel", () => { using var set = new ModSet(Open()); return IndexWithin(set.Mods, editorIds: true); }),
#if SAFEPATCH_BATCHES
    ("Mutagen: the same through EnumerateMajorRecordBatches", () => IndexBatches(warm.Mods)),
#endif
    ("Mutagen: full parse (CreateFromBinary) of every plugin", () =>
    {
        var counts = new long[order.Count];
        Parallel.For(0, order.Count, i => counts[i] = SkyrimMod.CreateFromBinary(new ModPath(order[i].Key, order[i].Path), SkyrimRelease.SkyrimSE).EnumerateMajorRecords().LongCount());
        return new Result(counts.Sum(), 0);
    }),
    ("Mutagen: 2,000 statics by key",() => new Result(statics.Count(s => s.Mod.Statics.TryGetValue(s.FormKey) is not null), 0)),
    ("Scanner: every record's FormKey and EditorID", () => Scanner(editorIds: true)),
};

foreach (var (name, run) in steps.Where(s => only is null || s.Name.Contains(only, StringComparison.OrdinalIgnoreCase)))
{
    var times = new List<double>();
    Result result = default;
    for (var i = 0; i < runs; i++)
    {
        GC.Collect();
        var clock = Stopwatch.StartNew();
        result = run();
        times.Add(clock.Elapsed.TotalMilliseconds);
    }
    var sorted = times.Order().ToList();
    var median = sorted[sorted.Count / 2];
    Console.WriteLine($"{name,-48} median {median,7:F0} ms  best {sorted[0],7:F0}  [first {times[0],7:F0}]  {result}");
    Console.WriteLine($"RESULT|{label}|{name}|{median:F0}|{sorted[0]:F0}|{result.Records}|{result.Checksum}");
}
return 0;

Result Index(ISkyrimModGetter[] mods, bool editorIds)
{
    var counts = new long[mods.Length];
    var hashes = new long[mods.Length];
    Parallel.For(0, mods.Length, i =>
    {
        long n = 0, h = 0;
        foreach (var record in mods[i].EnumerateMajorRecords())
        {
            n++;
            h += record.FormKey.GetHashCode() & 0xFFFF;
            if (editorIds) h += StringComparer.Ordinal.GetHashCode(record.EditorID ?? "") & 0xFFFF;
        }
        counts[i] = n;
        hashes[i] = h;
    });
    return new Result(counts.Sum(), hashes.Sum());
}

// The same visit as Index, with each plugin's records split into work items as the scanner splits them: each top-level
// group, and each block of cells (a worldspace's or the interior cells'), so one large plugin spreads over every core.
Result IndexWithin(ISkyrimModGetter[] mods, bool editorIds)
{
    var work = new List<Func<IEnumerable<IMajorRecordGetter>>>();
    var groups = typeof(ISkyrimModGetter).GetProperties().Where(p => typeof(IGroupGetter).IsAssignableFrom(p.PropertyType)).ToList();
    static IEnumerable<IMajorRecordGetter> WithChildren(IMajorRecordGetter record) =>
        record is IMajorRecordGetterEnumerable parent ? parent.EnumerateMajorRecords().Prepend(record) : [record];
    foreach (var mod in mods)
    {
        foreach (var property in groups)
        {
            if (property.Name == nameof(ISkyrimModGetter.Worldspaces)) continue;
            var group = (IGroupGetter)property.GetValue(mod)!;
            work.Add(() => group.Records.SelectMany(WithChildren));
        }
        foreach (var block in mod.Cells.Records)
        {
            foreach (var subBlock in block.SubBlocks) work.Add(() => subBlock.Cells.SelectMany(WithChildren));
        }
        foreach (var world in mod.Worldspaces)
        {
            work.Add(() => world.TopCell is { } top ? WithChildren(top).Prepend(world) : [world]);
            foreach (var block in world.SubCells)
            {
                foreach (var subBlock in block.Items) work.Add(() => subBlock.Items.SelectMany(WithChildren));
            }
        }
    }
    long n = 0, h = 0;
    Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(work, loadBalance: true), item =>
    {
        long localN = 0, localH = 0;
        foreach (var record in item())
        {
            localN++;
            localH += record.FormKey.GetHashCode() & 0xFFFF;
            if (editorIds) localH += StringComparer.Ordinal.GetHashCode(record.EditorID ?? "") & 0xFFFF;
        }
        Interlocked.Add(ref n, localN);
        Interlocked.Add(ref h, localH);
    });
    return new Result(n, h);
}

#if SAFEPATCH_BATCHES
Result IndexBatches(ISkyrimModGetter[] mods)
{
    long n = 0, h = 0;
    Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(mods.SelectMany(m => m.EnumerateMajorRecordBatches()).ToList(), loadBalance: true), batch =>
    {
        long localN = 0, localH = 0;
        foreach (var (record, _) in batch)
        {
            localN++;
            localH += (record.FormKey.GetHashCode() & 0xFFFF) + (StringComparer.Ordinal.GetHashCode(record.EditorID ?? "") & 0xFFFF);
        }
        Interlocked.Add(ref n, localN);
        Interlocked.Add(ref h, localH);
    });
    return new Result(n, h);
}
#endif

Result Scanner(bool editorIds)
{
    var counts = new long[order.Count];
    var hashes = new long[order.Count];
    Parallel.For(0, order.Count, i =>
    {
        var scan = PluginScanner.Scan(order[i].Path, editorIds: editorIds);
        var masters = scan.Masters.Select(m => ModKey.FromFileName(m)).ToArray();
        long h = 0;
        foreach (var r in scan.Records)
        {
            var index = (int)(r.FormId >> 24);
            var key = new FormKey(index < masters.Length ? masters[index] : order[i].Key, r.FormId & 0xFFFFFF);
            h += key.GetHashCode() & 0xFFFF;
            if (editorIds) h += StringComparer.Ordinal.GetHashCode(r.EditorId ?? "") & 0xFFFF;
        }
        counts[i] = scan.Records.Length;
        hashes[i] = h;
    });
    return new Result(counts.Sum(), hashes.Sum());
}

// Every record of some plugins: Mutagen's full parse against its overlay, reading the EditorID first. Prints how many
// records of each type differ, to compare between builds.
static int Check(string data, IReadOnlyList<string> plugins, string label)
{
    var differing = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var unequalToItself = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var editorIds = new SortedDictionary<string, int>(StringComparer.Ordinal);
    long total = 0;
    foreach (var plugin in plugins)
    {
        var path = new ModPath(Path.Combine(data, plugin));
        var full = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
        var again = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
        var reference = new Dictionary<FormKey, IMajorRecordGetter>();
        foreach (var record in full.EnumerateMajorRecords()) reference.TryAdd(record.FormKey, record);
        // Records equal to a second read of the same bytes: Equals itself must hold before reads can be compared.
        foreach (var record in again.EnumerateMajorRecords())
        {
            if (!reference[record.FormKey].Equals(record))
            {
                var type = record.GetType().Name;
                unequalToItself[type] = unequalToItself.GetValueOrDefault(type) + 1;
            }
        }
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        foreach (var record in overlay.EnumerateMajorRecords())
        {
            total++;
            var editorId = record.EditorID;
            var expected = reference[record.FormKey];
            var type = record.GetType().Name.Replace("BinaryOverlay", "");
            if (editorId != expected.EditorID)
            {
                editorIds[type] = editorIds.GetValueOrDefault(type) + 1;
                if (editorIds[type] == 1) Console.WriteLine($"EditorID of {record.FormKey} {type}: overlay {editorId ?? "(none)"}, full parse {expected.EditorID ?? "(none)"}");
            }
            if (editorId != expected.EditorID || !expected.Equals(record))
                differing[type] = differing.GetValueOrDefault(type) + 1;
        }
    }
    Console.WriteLine($"{total:N0} records in {string.Join(", ", plugins)}; differing from the full parse: " +
                      (differing.Count == 0 ? "none" : string.Join(", ", differing.Select(d => $"{d.Key} {d.Value}"))));
    Console.WriteLine("Unequal to a second full parse of themselves: " +
                      (unequalToItself.Count == 0 ? "none" : string.Join(", ", unequalToItself.Select(d => $"{d.Key} {d.Value}"))));
    Console.WriteLine("Of those, EditorID differs: " + (editorIds.Count == 0 ? "never" : string.Join(", ", editorIds.Select(d => $"{d.Key} {d.Value}"))));
    foreach (var (type, count) in differing) Console.WriteLine($"PARITY|{label}|{type}|{count}");
    foreach (var (type, count) in unequalToItself) Console.WriteLine($"SELF|{label}|{type}|{count}");
    Console.WriteLine($"PARITY|{label}|(total records)|{total}");
    return 0;
}

// Records equal by Equals must have equal hash codes: a second full parse, and the overlay, against the full parse.
static int CheckHash(string data, IReadOnlyList<string> plugins, string label)
{
    var differing = new SortedDictionary<string, int>(StringComparer.Ordinal);
    long equal = 0;
    foreach (var plugin in plugins)
    {
        var path = new ModPath(Path.Combine(data, plugin));
        var reference = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE).EnumerateMajorRecords()
            .GroupBy(r => r.FormKey).ToDictionary(g => g.Key, g => g.First());
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        var again = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
        foreach (var record in again.EnumerateMajorRecords().Concat(overlay.EnumerateMajorRecords()))
        {
            var expected = reference[record.FormKey];
            if (!expected.Equals(record)) continue;
            equal++;
            if (expected.GetHashCode() == record.GetHashCode()) continue;
            var type = record.GetType().Name.Replace("BinaryOverlay", "");
            differing[type] = differing.GetValueOrDefault(type) + 1;
        }
    }
    Console.WriteLine($"{equal:N0} equal pairs (second full parse and overlay) in {string.Join(", ", plugins)}; hash differs: " +
                      (differing.Count == 0 ? "never" : string.Join(", ", differing.Select(d => $"{d.Key} {d.Value}"))));
    foreach (var (type, count) in differing) Console.WriteLine($"HASH|{label}|{type}|{count}");
    Console.WriteLine($"HASH|{label}|(equal pairs)|{equal}");
    return 0;
}

// Many threads reading the same fresh overlay records at once: each must see the complete record.
static int CheckThreads(string data, string plugin)
{
    var path = new ModPath(Path.Combine(data, plugin));
    var full = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
    var reference = new Dictionary<FormKey, IMajorRecordGetter>();
    foreach (var record in full.EnumerateMajorRecords()) reference.TryAdd(record.FormKey, record);
    var baseline = new ConcurrentDictionary<FormKey, bool>();
    using (var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE))
        foreach (var record in overlay.EnumerateMajorRecords()) baseline[record.FormKey] = reference[record.FormKey].Equals(record);

    long mismatches = 0, checks = 0;
    for (var round = 0; round < 3; round++)
    {
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        var records = overlay.EnumerateMajorRecords().ToList();
        Parallel.ForEach(records, new ParallelOptions { MaxDegreeOfParallelism = 16 }, record =>
        {
            // Eight readers race to complete the same record's deferred fill.
            var results = new bool[8];
            Parallel.For(0, results.Length, i => results[i] = reference[record.FormKey].Equals(record) && record.EditorID == reference[record.FormKey].EditorID);
            Interlocked.Add(ref checks, results.Length);
            foreach (var result in results)
                if (result != baseline[record.FormKey]) Interlocked.Increment(ref mismatches);
        });
    }
    Console.WriteLine($"{checks:N0} concurrent reads of {plugin} records: {mismatches} differed from a single-threaded read.");
    return mismatches == 0 ? 0 : 1;
}

internal readonly record struct Result(long Records, long Checksum);

internal sealed class ModSet(ISkyrimModGetter[] mods) : IDisposable
{
    public ISkyrimModGetter[] Mods => mods;
    public void Dispose()
    {
        foreach (var mod in mods) (mod as IDisposable)?.Dispose();
    }
}

namespace SafePatch.Host
{
    /// <summary>The scanner's error type, outside SafePatch.</summary>
    public class SafePatchException(string message) : Exception(message);
}
