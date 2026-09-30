using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

/// <summary>
/// Memory of one scenario over a load order, in a process of its own (a process's peak cannot be reset):
/// <list type="bullet">
/// <item><c>open</c>: every plugin opened as an overlay and every record's FormKey read, keeping only the plugins;</item>
/// <item><c>keep</c>: every record object kept, nothing read but what enumeration reads;</item>
/// <item><c>keep-read</c>: every record object kept, and every field of every record read (see <see cref="ReadFields"/>);</item>
/// <item><c>full</c>: every plugin read by <c>CreateFromBinary</c> and kept.</item>
/// </list>
/// Prints one <c>MEMORY|</c> line: records, the heap the scenario keeps alive (after a full collection, against the
/// heap before it), the process's peak working set and peak private bytes, the bytes allocated, and the time.
/// </summary>
internal static class Memory
{
    public static int Run(string data, string pluginsTxt, string scenario, string label)
    {
        var listings = LoadOrder.GetLoadOrderListings(GameRelease.SkyrimSE, pluginsTxt, null, data, throwOnMissingMods: false)
            .Where(l => l.Enabled && File.Exists(Path.Combine(data, l.ModKey.FileName))).ToList();
        var order = listings.Select(l => new ModPath(l.ModKey, Path.Combine(data, l.ModKey.FileName))).ToArray();

        var heapBefore = GC.GetTotalMemory(forceFullCollection: true);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var clock = Stopwatch.StartNew();
        object kept;
        long records;
        long fieldsRead = 0;
        switch (scenario)
        {
            case "open":
            {
                var mods = Open(order);
                var counts = new long[mods.Length];
                Parallel.For(0, mods.Length, i => counts[i] = mods[i].EnumerateMajorRecords().LongCount(r => !r.FormKey.IsNull));
                records = counts.Sum();
                kept = mods;
                break;
            }
            case "keep":
            case "keep-read":
            {
                var mods = Open(order);
                var list = mods.SelectMany(m => m.EnumerateMajorRecords()).ToList();
                if (scenario == "keep-read")
                {
                    Parallel.ForEach(Partitioner.Create(0, list.Count, 4096), range =>
                    {
                        long read = 0;
                        for (var i = range.Item1; i < range.Item2; i++) read += ReadFields(list[i]);
                        Interlocked.Add(ref fieldsRead, read);
                    });
                }
                records = list.Count;
                kept = (mods, list);
                break;
            }
            case "full":
            {
                var mods = new ISkyrimModGetter[order.Length];
                Parallel.For(0, order.Length, i => mods[i] = SkyrimMod.CreateFromBinary(order[i], SkyrimRelease.SkyrimSE));
                records = mods.Sum(m => m.EnumerateMajorRecords().LongCount());
                kept = mods;
                break;
            }
            default:
                Console.Error.WriteLine($"Unknown scenario {scenario}: open, keep, keep-read or full.");
                return 2;
        }
        var elapsed = clock.Elapsed;
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var retained = GC.GetTotalMemory(forceFullCollection: true) - heapBefore;
        GC.KeepAlive(kept);

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        const double MiB = 1024 * 1024;
        foreach (var failure in Failures.Keys.Order()) Console.WriteLine($"  unreadable: {failure}");
        Console.WriteLine($"{scenario,-10} {records:N0} records, {fieldsRead:N0} fields read: retained {retained / MiB:N0} MiB, " +
                          $"peak working set {process.PeakWorkingSet64 / MiB:N0} MiB, peak private {process.PeakPagedMemorySize64 / MiB:N0} MiB, " +
                          $"allocated {allocated / MiB:N0} MiB, {elapsed.TotalMilliseconds:N0} ms");
        Console.WriteLine($"MEMORY|{label}|{scenario}|{records}|{fieldsRead}|{retained / MiB:F0}|{process.PeakWorkingSet64 / MiB:F0}|" +
                          $"{process.PeakPagedMemorySize64 / MiB:F0}|{allocated / MiB:F0}|{elapsed.TotalMilliseconds:F0}");
        return 0;
    }

    private static ISkyrimModGetter[] Open(ModPath[] order) =>
        [.. order.AsParallel().AsOrdered().Select(o => SkyrimMod.CreateFromBinaryOverlay(o, SkyrimRelease.SkyrimSE))];

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> FieldsByType = new();
    private static readonly ConcurrentDictionary<string, byte> Failures = new();

    /// <summary>
    /// Reads every public property of a record, as a caller reading all its fields would, and enumerates every list
    /// property. Properties holding other major records (a cell's references, a topic's responses, a worldspace's
    /// blocks) are left out: those records are kept and read in their own right. Returns the values read.
    /// </summary>
    private static long ReadFields(IMajorRecordGetter record)
    {
        long read = 0;
        foreach (var property in FieldsByType.GetOrAdd(record.GetType(), FieldsOf))
        {
            object? value;
            try
            {
                value = property.GetValue(record);
            }
            catch (TargetInvocationException ex)
            {
                Failures.TryAdd($"{property.DeclaringType?.Name}.{property.Name}: {ex.InnerException?.GetType().Name}", 0);
                continue;
            }
            read++;
            if (value is IEnumerable items and not string and not IEnumerable<byte>)
            {
                foreach (var _ in items) read++;
            }
        }
        return read;
    }

    private static PropertyInfo[] FieldsOf(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && p.Name != "Registration" && !HoldsRecords(p.PropertyType))];

    private static bool HoldsRecords(Type type)
    {
        static bool Holds(Type t) => typeof(IMajorRecordGetter).IsAssignableFrom(t) || typeof(IMajorRecordGetterEnumerable).IsAssignableFrom(t);
        if (Holds(type)) return true;
        return type.GetInterfaces().Append(type).Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>) && Holds(i.GetGenericArguments()[0]));
    }
}