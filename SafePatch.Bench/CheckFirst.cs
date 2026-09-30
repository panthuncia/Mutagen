using System.Collections;
using System.Reflection;
using Loqui;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

/// <summary>
/// For records whose fill is deferred: every property, read first on a record fresh from its group (so nothing has
/// completed its fill yet), equals the same property of a full parse. A member the fill sets that is read without
/// completing the fill reads as empty here.
/// <code>SafePatch.Bench check-first &lt;Data folder&gt; &lt;plugin,...&gt; &lt;record type,...&gt;</code>
/// </summary>
internal static class CheckFirst
{
    public static int Run(string data, IReadOnlyList<string> plugins, IReadOnlyList<string> typeNames)
    {
        long checkedValues = 0, records = 0;
        var failures = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var plugin in plugins)
        {
            var path = new ModPath(Path.Combine(data, plugin));
            var full = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
            using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
            foreach (var typeName in typeNames)
            {
                var getter = typeof(ISkyrimModGetter).Assembly.GetType($"Mutagen.Bethesda.Skyrim.I{typeName}Getter")
                             ?? throw new ArgumentException($"No record type {typeName}");
                var group = overlay.TryGetTopLevelGroup(getter) ?? throw new ArgumentException($"{typeName} has no top-level group");
                var expected = full.EnumerateMajorRecords(getter).ToDictionary(r => r.FormKey);
                var properties = getter.GetInterfaces().Prepend(getter).SelectMany(i => i.GetProperties())
                    .Where(p => p.GetIndexParameters().Length == 0 && p.Name != "Registration" && p.Name != "ExportingExtraNam3")
                    .DistinctBy(p => p.Name).ToArray();
                foreach (var formKey in group.FormKeys)
                {
                    records++;
                    foreach (var property in properties)
                    {
                        var fresh = group[formKey];
                        object? actual;
                        try { actual = property.GetValue(fresh); }
                        catch (TargetInvocationException ex) { Fail(failures, typeName, property.Name, ex.InnerException!.GetType().Name); continue; }
                        var want = property.GetValue(expected[formKey]);
                        checkedValues++;
                        if (!Same(actual, want))
                        {
                            Fail(failures, typeName, property.Name, "differs");
                            if (Shown.Add($"{typeName}.{property.Name}") && Environment.GetEnvironmentVariable("CHECK_FIRST_SHOW") == "1")
                                Console.WriteLine($"  {formKey} {typeName}.{property.Name}: read first {Show(actual)}; full parse {Show(want)}");
                        }
                    }
                }
            }
            Console.WriteLine($"{plugin}: {records:N0} records so far");
        }
        Console.WriteLine($"{records:N0} records, {checkedValues:N0} properties read first; differing: " +
                          (failures.Count == 0 ? "none" : string.Join(", ", failures.Select(f => $"{f.Key} {f.Value}"))));
        return failures.Count == 0 ? 0 : 1;
    }

    private static readonly HashSet<string> Shown = [];

    private static string Show(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        IEnumerable items and not string => $"[{string.Join(", ", items.Cast<object?>().Take(6).Select(Show))}]",
        _ => value.ToString() ?? "?",
    };

    private static void Fail(SortedDictionary<string, int> failures, string type, string property, string why)
    {
        var key = $"{type}.{property} ({why})";
        failures[key] = failures.GetValueOrDefault(key) + 1;
    }

    /// <summary>Equal values: by the generated Equals for records' parts, element by element for collections.</summary>
    private static bool Same(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null || IsEmpty(a) && IsEmpty(b);
        if (a is string || a.GetType().IsPrimitive || a is Enum) return a.Equals(b);
        if (a is IDictionary da && b is IDictionary db)
        {
            if (da.Count != db.Count) return false;
            foreach (DictionaryEntry e in da) if (!db.Contains(e.Key) || !Same(e.Value, db[e.Key])) return false;
            return true;
        }
        if (a is ILoquiObject || a is IFormLinkIdentifier || a is Mutagen.Bethesda.Strings.ITranslatedStringGetter) return a.Equals(b) || b.Equals(a);
        if (a is IEnumerable ea && b is IEnumerable eb && a is not string)
        {
            var la = ea.Cast<object?>().ToList();
            var lb = eb.Cast<object?>().ToList();
            return la.Count == lb.Count && la.Zip(lb).All(p => Same(p.First, p.Second));
        }
        return a.Equals(b) || b.Equals(a);
    }

    private static bool IsEmpty(object? value) => value is null || value is IEnumerable e && value is not string && !e.Cast<object>().Any();
}