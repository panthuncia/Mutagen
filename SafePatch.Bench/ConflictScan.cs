using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using Loqui;
using Loqui.Internal;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

/// <summary>
/// Conflict enumeration as an editor would do it on a load order that is already open: index every plugin's records
/// with <c>EnumerateMajorRecordBatches</c> on every core, keep the records overridden at least once, and compare each
/// override with the version before it. Each run indexes fresh record objects, so it reads and parses what it compares.
/// The comparison is one of:
/// <list type="bullet">
/// <item><c>mask</c>: the generated equals mask, every field with its full result;</item>
/// <item><c>fields</c>: which top-level fields differ, through the generated <c>Equals</c> restricted to one field at a
/// time (no allocation; what a generated "differing fields" method would do, a little slower);</item>
/// <item><c>equals</c>: the generated <c>Equals</c>, stopping at the first difference.</item>
/// </list>
/// Header bookkeeping (version control, form version) and child records are left out of <c>fields</c> and
/// <c>equals</c>, as a conflict view leaves them out.
/// <code>SafePatch.Bench conflict-scan &lt;Data folder&gt; &lt;plugins.txt or .paths&gt; [runs=3] [label] [mask|fields|equals]</code>
/// Batches need a Mutagen with <c>EnumerateMajorRecordBatches</c> (build with <c>-p:SafePatchBatches=true</c>).
/// </summary>
internal static class ConflictScan
{
    private static readonly HashSet<string> Bookkeeping = ["VersionControl", "FormVersion", "Version2"];

    public static int Run(string data, string pluginsTxt, int runs, string label, string comparer)
    {
#if SAFEPATCH_BATCHES
        var clock = Stopwatch.StartNew();
        var mods = Conflicts.Paths(data, pluginsTxt).AsParallel().AsOrdered()
            .Select(path => SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE))
            .ToArray();
        Console.WriteLine($"{mods.Length} plugins opened in {clock.ElapsedMilliseconds:N0} ms; comparer {comparer}, server GC {System.Runtime.GCSettings.IsServerGC}");
        var compare = comparer switch
        {
            "mask" => (Func<IMajorRecordGetter, IMajorRecordGetter, int>)((a, b) => Masks.GetOrAdd(ClassOf(a), BuildMask)(a, b) ? 0 : 1),
            "fields" => (a, b) => DifferingFields(a, b),
            "equals" => (a, b) => Equalities.GetOrAdd(ClassOf(a), BuildEquals)(a, b) ? 0 : 1,
            _ => throw new ArgumentException($"Unknown comparer {comparer}"),
        };

        for (var run = 0; run < runs; run++)
        {
            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var collections = GC.CollectionCount(0);

            // Index: every plugin's batches on every core, then the versions of each FormKey in load order, sharded by
            // FormKey so the shards build in parallel too.
            clock.Restart();
            var batches = mods.SelectMany((mod, plugin) => mod.EnumerateMajorRecordBatches().Select(batch => (Plugin: plugin, Records: batch))).ToArray();
            var read = new (FormKey FormKey, IMajorRecordGetter Record)[batches.Length][];
            Parallel.For(0, batches.Length, b => read[b] = [.. batches[b].Records.Select(r => (r.Record.FormKey, r.Record))]);
            const int Shards = 64;
            var chains = new List<IMajorRecordGetter>[Shards][];
            Parallel.For(0, Shards, shard =>
            {
                var versions = new Dictionary<FormKey, List<IMajorRecordGetter>>();
                foreach (var batch in read)
                {
                    foreach (var (formKey, record) in batch)
                    {
                        if ((formKey.GetHashCode() & (Shards - 1)) != shard) continue;
                        if (!versions.TryGetValue(formKey, out var chain)) versions[formKey] = chain = new List<IMajorRecordGetter>(1);
                        chain.Add(record);
                    }
                }
                chains[shard] = [.. versions.Values.Where(c => c.Count > 1)];
            });
            var overridden = chains.SelectMany(c => c).ToArray();
            var records = read.Sum(r => (long)r.Length);
            read = null;
            var indexed = clock.Elapsed;

            var contentions = Monitor.LockContentionCount;
            clock.Restart();
            long pairs = 0, differing = 0, failed = 0;
            Parallel.ForEach(Partitioner.Create(0, overridden.Length, 64), range =>
            {
                long localPairs = 0, localDiffering = 0, localFailed = 0;
                for (var c = range.Item1; c < range.Item2; c++)
                {
                    var chain = overridden[c];
                    for (var i = 1; i < chain.Count; i++)
                    {
                        localPairs++;
                        if (ClassOf(chain[i - 1]) != ClassOf(chain[i])) continue;
                        try
                        {
                            localDiffering += compare(chain[i - 1], chain[i]);
                        }
                        catch (Exception)
                        {
                            localFailed++;
                        }
                    }
                }
                Interlocked.Add(ref pairs, localPairs);
                Interlocked.Add(ref differing, localDiffering);
                Interlocked.Add(ref failed, localFailed);
            });
            var compared = clock.Elapsed;
            var contended = Monitor.LockContentionCount - contentions;
            var mib = (GC.GetTotalAllocatedBytes(precise: true) - allocated) / (1024 * 1024);
            var gcs = GC.CollectionCount(0) - collections;
            Console.WriteLine($"run {run + 1}: {records:N0} versions, {overridden.Length:N0} overridden, {pairs:N0} pairs, {differing:N0} differing ({failed} failed): " +
                              $"index {indexed.TotalMilliseconds:N0} ms, compare {compared.TotalMilliseconds:N0} ms; {mib:N0} MiB allocated, {gcs} gen0 collections, {contended:N0} contended locks while comparing");
            Console.WriteLine($"SCAN|{label}|{comparer}|{run + 1}|{records}|{pairs}|{differing}|{indexed.TotalMilliseconds:F0}|{compared.TotalMilliseconds:F0}|{mib}|{gcs}");
        }
        foreach (var mod in mods) (mod as IDisposable)?.Dispose();
        return 0;
#else
        Console.Error.WriteLine("conflict-scan needs EnumerateMajorRecordBatches: build with -p:SafePatchBatches=true.");
        return 2;
#endif
    }

    private static Type ClassOf(IMajorRecordGetter record) => ((ILoquiObject)record).Registration.ClassType;

    private static readonly ConcurrentDictionary<Type, Func<IMajorRecordGetter, IMajorRecordGetter, bool>> Masks = new();
    private static readonly ConcurrentDictionary<Type, Func<IMajorRecordGetter, IMajorRecordGetter, bool>> Equalities = new();
    private static readonly ConcurrentDictionary<Type, Func<IMajorRecordGetter, IMajorRecordGetter, bool>[]> FieldComparers = new();

    private static int DifferingFields(IMajorRecordGetter a, IMajorRecordGetter b)
    {
        var count = 0;
        foreach (var field in FieldComparers.GetOrAdd(ClassOf(a), BuildFields))
        {
            if (!field(a, b)) count++;
        }
        return count > 0 ? 1 : 0;
    }

    private static Func<IMajorRecordGetter, IMajorRecordGetter, bool> BuildMask(Type classType)
    {
        var registration = LoquiRegistration.GetRegister(classType);
        var mixIn = classType.Assembly.GetType(classType.FullName + "MixIn")!;
        var method = mixIn.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "GetEqualsMask" && m.GetParameters() is [var p, var q, _]
                         && p.ParameterType == registration.GetterType && q.ParameterType == registration.GetterType);
        var (a, b, getterA, getterB) = Parameters(registration);
        var mask = Expression.Call(method, getterA, getterB, Expression.Constant(Enum.Parse(method.GetParameters()[2].ParameterType, "All")));
        Func<bool, bool> isTrue = x => x;
        var body = Expression.Call(mask, mask.Type.GetMethod("All", [typeof(Func<bool, bool>)])!, Expression.Constant(isTrue));
        return Expression.Lambda<Func<IMajorRecordGetter, IMajorRecordGetter, bool>>(body, a, b).Compile();
    }

    /// <summary><c>Equals</c> with bookkeeping and child records left out.</summary>
    private static Func<IMajorRecordGetter, IMajorRecordGetter, bool> BuildEquals(Type classType)
    {
        var fields = MaskFields(classType);
        return CommonEquals(classType, Crystal(classType, fields.Where(f => f.Include).Select(f => f.Field)));
    }

    /// <summary>One <c>Equals</c> per compared top-level field, each restricted to that field.</summary>
    private static Func<IMajorRecordGetter, IMajorRecordGetter, bool>[] BuildFields(Type classType) =>
        [.. MaskFields(classType).Where(f => f.Include).Select(f => CommonEquals(classType, Crystal(classType, [f.Field])))];

    private static (FieldInfo Field, bool Include)[] MaskFields(Type classType)
    {
        var maskType = classType.GetNestedType("TranslationMask")!;
        var children = ChildFields(classType);
        return [.. maskType.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(bool) || f.FieldType.Name == "TranslationMask")
            .Select(f => (f, !Bookkeeping.Contains(f.Name) && !children.Contains(f.Name)))];
    }

    private static TranslationCrystal Crystal(Type classType, IEnumerable<FieldInfo> on)
    {
        var maskType = classType.GetNestedType("TranslationMask")!;
        var mask = System.Activator.CreateInstance(maskType, false, true)!;
        foreach (var field in on)
        {
            field.SetValue(mask, field.FieldType == typeof(bool) ? true : System.Activator.CreateInstance(field.FieldType, true, true));
        }
        return (TranslationCrystal)maskType.GetMethod("GetCrystal", Type.EmptyTypes)!.Invoke(mask, null)!;
    }

    /// <summary>The common class's <c>Equals(lhs, rhs, crystal)</c>, so no crystal is built per call.</summary>
    private static Func<IMajorRecordGetter, IMajorRecordGetter, bool> CommonEquals(Type classType, TranslationCrystal crystal)
    {
        var registration = LoquiRegistration.GetRegister(classType);
        var common = classType.Assembly.GetType(classType.FullName + "Common")!;
        var instance = common.GetField("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var method = common.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Equals" && m.GetParameters() is [var p, var q, var r]
                        && p.ParameterType == registration.GetterType && q.ParameterType == registration.GetterType && r.ParameterType == typeof(TranslationCrystal));
        var (a, b, getterA, getterB) = Parameters(registration);
        var body = Expression.Call(Expression.Constant(instance), method, getterA, getterB, Expression.Constant(crystal));
        return Expression.Lambda<Func<IMajorRecordGetter, IMajorRecordGetter, bool>>(body, a, b).Compile();
    }

    private static (ParameterExpression, ParameterExpression, Expression, Expression) Parameters(ILoquiRegistration registration)
    {
        var a = Expression.Parameter(typeof(IMajorRecordGetter), "a");
        var b = Expression.Parameter(typeof(IMajorRecordGetter), "b");
        return (a, b, Expression.Convert(a, registration.GetterType), Expression.Convert(b, registration.GetterType));
    }

    private static HashSet<string> ChildFields(Type classType)
    {
        var getter = LoquiRegistration.GetRegister(classType).GetterType;
        return [.. getter.GetInterfaces().Prepend(getter).SelectMany(i => i.GetProperties())
            .Where(p => HoldsRecords(p.PropertyType, [])).Select(p => p.Name)];
    }

    private static bool HoldsRecords(Type type, HashSet<Type> seen)
    {
        if (typeof(IMajorRecordGetter).IsAssignableFrom(type)) return true;
        if (type == typeof(string) || !seen.Add(type)) return false;
        var element = type.IsArray ? type.GetElementType() : type.GetInterfaces().Prepend(type)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
        if (element is not null) return HoldsRecords(element, seen);
        return typeof(ILoquiObject).IsAssignableFrom(type)
               && type.GetInterfaces().Prepend(type).SelectMany(i => i.GetProperties()).Any(p => HoldsRecords(p.PropertyType, seen));
    }
}