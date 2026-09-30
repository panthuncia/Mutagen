using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using Loqui;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

/// <summary>
/// Conflict enumeration over a load order, as an editor's conflict view does it: every record's versions are
/// collected in load order, and each override is compared with the version before it, reading every field of both.
/// A record type without child records is compared by its generated equals mask (every field, always); a cell,
/// worldspace or topic by its generated <c>Equals</c> with a translation mask leaving its child records out (those are
/// compared as records of their own). The plugins are opened once; each run collects fresh record objects, so every
/// run reads and parses the records it compares.
/// <code>SafePatch.Bench conflicts|conflicts-self &lt;Data folder&gt; &lt;plugins.txt&gt; [runs=3] [label]</code>
/// </summary>
internal static class Conflicts
{
    /// <param name="self">
    /// Compare every record with the same record read from a second opening of the plugins, rather than overrides with
    /// the versions before them: every record is compared, and every pair is equal, so every field of both is read.
    /// This stands in for a heavily modded load order, where most of the records are overridden.
    /// </param>
    public static int Run(string data, string pluginsTxt, int runs, string label, bool self)
    {
        var listings = LoadOrder.GetLoadOrderListings(GameRelease.SkyrimSE, pluginsTxt, null, data, throwOnMissingMods: false)
            .Where(l => l.Enabled && File.Exists(Path.Combine(data, l.ModKey.FileName))).ToList();
        var mods = listings.AsParallel().AsOrdered()
            .Select(l => SkyrimMod.CreateFromBinaryOverlay(new ModPath(l.ModKey, Path.Combine(data, l.ModKey.FileName)), SkyrimRelease.SkyrimSE))
            .ToArray();

        if (self) return RunSelf(data, listings, mods, runs, label);
        for (var run = 0; run < runs; run++)
        {
            GC.Collect();
            var clock = Stopwatch.StartNew();
            var perPlugin = new List<IMajorRecordGetter>[mods.Length];
            Parallel.For(0, mods.Length, i => perPlugin[i] = [.. mods[i].EnumerateMajorRecords()]);
            var versions = new Dictionary<FormKey, List<IMajorRecordGetter>>();
            foreach (var pluginRecords in perPlugin)
            {
                foreach (var record in pluginRecords)
                {
                    if (!versions.TryGetValue(record.FormKey, out var chain)) versions[record.FormKey] = chain = [];
                    chain.Add(record);
                }
            }
            var chains = versions.Values.Where(c => c.Count > 1).ToArray();
            var indexed = clock.Elapsed;

            clock.Restart();
            long pairs = 0, identical = 0;
            Parallel.ForEach(Partitioner.Create(0, chains.Length, 64), range =>
            {
                long localPairs = 0, localIdentical = 0;
                for (var c = range.Item1; c < range.Item2; c++)
                {
                    var chain = chains[c];
                    for (var i = 1; i < chain.Count; i++)
                    {
                        localPairs++;
                        if (Same(chain[i - 1], chain[i])) localIdentical++;
                    }
                }
                Interlocked.Add(ref pairs, localPairs);
                Interlocked.Add(ref identical, localIdentical);
            });
            var compared = clock.Elapsed;

            var records = perPlugin.Sum(p => (long)p.Count);
            Console.WriteLine($"conflicts run {run + 1}: {records:N0} versions, {chains.Length:N0} overridden records, {pairs:N0} pairs " +
                              $"({identical:N0} identical): collect {indexed.TotalMilliseconds:N0} ms, compare {compared.TotalMilliseconds:N0} ms");
            Console.WriteLine($"CONFLICTS|{label}|{run + 1}|{records}|{chains.Length}|{pairs}|{identical}|{indexed.TotalMilliseconds:F0}|{compared.TotalMilliseconds:F0}");
        }
        foreach (var mod in mods) (mod as IDisposable)?.Dispose();
        return 0;
    }

    private static int RunSelf(string data, List<ILoadOrderListingGetter> listings, ISkyrimModGetter[] mods, int runs, string label)
    {
        var twins = listings.AsParallel().AsOrdered()
            .Select(l => SkyrimMod.CreateFromBinaryOverlay(new ModPath(l.ModKey, Path.Combine(data, l.ModKey.FileName)), SkyrimRelease.SkyrimSE))
            .ToArray();
        for (var run = 0; run < runs; run++)
        {
            GC.Collect();
            var clock = Stopwatch.StartNew();
            var left = new List<IMajorRecordGetter>[mods.Length];
            var right = new List<IMajorRecordGetter>[mods.Length];
            Parallel.For(0, mods.Length * 2, i =>
            {
                if (i < mods.Length) left[i] = [.. mods[i].EnumerateMajorRecords()];
                else right[i - mods.Length] = [.. twins[i - mods.Length].EnumerateMajorRecords()];
            });
            var pairsToCompare = left.Zip(right).SelectMany(p => p.First.Zip(p.Second)).ToArray();
            var collected = clock.Elapsed;

            clock.Restart();
            long identical = 0;
            Parallel.ForEach(Partitioner.Create(0, pairsToCompare.Length, 1024), range =>
            {
                long localIdentical = 0;
                for (var i = range.Item1; i < range.Item2; i++)
                {
                    var (a, b) = pairsToCompare[i];
                    if (a.FormKey != b.FormKey) throw new InvalidOperationException($"{a.FormKey} paired with {b.FormKey}.");
                    if (Same(a, b)) localIdentical++;
                }
                Interlocked.Add(ref identical, localIdentical);
            });
            var compared = clock.Elapsed;
            Console.WriteLine($"conflicts-self run {run + 1}: {pairsToCompare.Length:N0} pairs ({identical:N0} identical): " +
                              $"collect {collected.TotalMilliseconds:N0} ms, compare {compared.TotalMilliseconds:N0} ms");
            Console.WriteLine($"CONFLICTS-SELF|{label}|{run + 1}|{pairsToCompare.Length}|{identical}|{collected.TotalMilliseconds:F0}|{compared.TotalMilliseconds:F0}");
        }
        foreach (var mod in mods.Concat(twins)) (mod as IDisposable)?.Dispose();
        return 0;
    }

    private static readonly ConcurrentDictionary<Type, Func<IMajorRecordGetter, IMajorRecordGetter, bool>> Comparers = new();

    private static bool Same(IMajorRecordGetter a, IMajorRecordGetter b)
    {
        var type = ((ILoquiObject)a).Registration.ClassType;
        return type == ((ILoquiObject)b).Registration.ClassType && Comparers.GetOrAdd(type, BuildComparer)(a, b);
    }

    private static Func<IMajorRecordGetter, IMajorRecordGetter, bool> BuildComparer(Type classType)
    {
        var registration = LoquiRegistration.GetRegister(classType);
        var mixIn = classType.Assembly.GetType(classType.FullName + "MixIn")
                    ?? throw new NotSupportedException($"No mix-in for {classType.Name}.");
        var a = Expression.Parameter(typeof(IMajorRecordGetter), "a");
        var b = Expression.Parameter(typeof(IMajorRecordGetter), "b");
        var getterA = Expression.Convert(a, registration.GetterType);
        var getterB = Expression.Convert(b, registration.GetterType);
        var children = AllProperties(registration.GetterType).Where(p => HoldsRecords(p.PropertyType, [])).Select(p => p.Name).ToList();
        Expression body;
        if (children.Count == 0)
        {
            var method = mixIn.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "GetEqualsMask" && m.GetParameters() is [var p, var q, _]
                             && p.ParameterType == registration.GetterType && q.ParameterType == registration.GetterType);
            var include = Enum.Parse(method.GetParameters()[2].ParameterType, "All");
            var mask = Expression.Call(method, getterA, getterB, Expression.Constant(include));
            Func<bool, bool> isTrue = x => x;
            body = Expression.Call(mask, mask.Type.GetMethod("All", [typeof(Func<bool, bool>)])!, Expression.Constant(isTrue));
        }
        else
        {
            var maskType = classType.GetNestedType("TranslationMask")!;
            var translationMask = System.Activator.CreateInstance(maskType, true, true)!;
            foreach (var name in children)
            {
                var field = maskType.GetField(name)!;
                field.SetValue(translationMask, field.FieldType == typeof(bool) ? false : System.Activator.CreateInstance(field.FieldType, false, false));
            }
            var method = mixIn.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "Equals" && m.GetParameters() is [var p, var q, var r]
                             && p.ParameterType == registration.GetterType && q.ParameterType == registration.GetterType && r.ParameterType == maskType);
            body = Expression.Call(method, getterA, getterB, Expression.Constant(translationMask, maskType));
        }
        return Expression.Lambda<Func<IMajorRecordGetter, IMajorRecordGetter, bool>>(body, a, b).Compile();
    }

    private static bool HoldsRecords(Type type, HashSet<Type> seen)
    {
        if (typeof(IMajorRecordGetter).IsAssignableFrom(type)) return true;
        if (type == typeof(string) || !seen.Add(type)) return false;
        var element = type.IsArray ? type.GetElementType() : EnumerableElement(type);
        if (element is not null) return HoldsRecords(element, seen);
        return typeof(ILoquiObject).IsAssignableFrom(type) && AllProperties(type).Any(p => HoldsRecords(p.PropertyType, seen));
    }

    private static Type? EnumerableElement(Type type) =>
        (type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? type : null)
        ?.GetGenericArguments()[0]
        ?? type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];

    private static IEnumerable<PropertyInfo> AllProperties(Type type) =>
        type.IsInterface
            ? type.GetInterfaces().Prepend(type).SelectMany(i => i.GetProperties(BindingFlags.Public | BindingFlags.Instance)).DistinctBy(p => p.Name)
            : type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
}