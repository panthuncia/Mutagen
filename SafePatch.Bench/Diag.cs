using System.Collections;
using Loqui;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

/// <summary>
/// For records whose two reads disagree, the properties to blame: `diag` compares the full parse with the overlay,
/// `diag-self` two full parses of the same bytes.
/// </summary>
internal static class Diag
{
    public static int Run(string data, string plugin, string type, int max, bool self)
    {
        var path = new ModPath(Path.Combine(data, plugin));
        var full = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
        var reference = full.EnumerateMajorRecords().GroupBy(r => r.FormKey).ToDictionary(g => g.Key, g => g.First());
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        IEnumerable<IMajorRecordGetter> others = self
            ? SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE).EnumerateMajorRecords()
            : overlay.EnumerateMajorRecords();
        var shown = 0;
        foreach (var b in others)
        {
            if (!b.GetType().Name.StartsWith(type, StringComparison.Ordinal) || b.GetType().Name.Replace("BinaryOverlay", "") != type) continue;
            var a = reference[b.FormKey];
            if (a.Equals(b) && a.EditorID == b.EditorID) continue;
            if (shown++ == max) break;
            Console.WriteLine($"{b.FormKey} {type} {a.EditorID}");
            Compare("", a, b, 1);
        }
        return 0;
    }

    static string Show(object? v) => v switch
    {
        null => "null",
        string s => '"' + s + '"',
        byte[] bytes => Convert.ToHexString(bytes),
        Noggog.ReadOnlyMemorySlice<byte> slice => Convert.ToHexString(slice.ToArray()),
        IEnumerable e => "[" + string.Join(", ", e.Cast<object?>().Select(Show)) + "]",
        Enum en => $"{en} ({Convert.ToInt64(en)})",
        float f => f.ToString("R"),
        _ => v.ToString()!.ReplaceLineEndings(" "),
    };

    static void Compare(string name, object? x, object? y, int depth)
    {
        var pad = new string(' ', depth * 2);
        if (depth < 7 && x is ILoquiObject lx && y is ILoquiObject)
        {
            var getter = lx.Registration.GetterType;
            var props = getter.GetInterfaces().Prepend(getter).SelectMany(i => i.GetProperties())
                .DistinctBy(p => p.Name).Where(p => p.GetIndexParameters().Length == 0 && p.PropertyType != typeof(Type) && p.PropertyType != typeof(ILoquiRegistration));
            var any = false;
            foreach (var prop in props)
            {
                object? px, py;
                try { px = prop.GetValue(x); py = prop.GetValue(y); }
                catch (Exception e) { Console.WriteLine($"{pad}{name}.{prop.Name}: threw {e.GetType().Name}"); continue; }
                if (Same(px, py)) continue;
                any = true;
                Compare(name + "." + prop.Name, px, py, depth + 1);
            }
            if (!any)
            {
                Console.WriteLine($"{pad}{name}: no property differs, yet Equals is false ({x.GetType().Name} vs {y.GetType().Name})");
                // The usual suspects: values equal only by content, and NaN, which EqualsWithin never finds equal.
                foreach (var prop in props)
                {
                    object? px, py;
                    try { px = prop.GetValue(x); py = prop.GetValue(y); } catch { continue; }
                    if (px is float.NaN || px is double.NaN) Console.WriteLine($"{pad}  {prop.Name} is NaN");
                    else if (!Equals(px, py) && px is not null) Console.WriteLine($"{pad}  {prop.Name}: equal by content only ({px.GetType().Name} vs {py?.GetType().Name})");
                }
            }
            return;
        }
        if (depth < 7 && x is IEnumerable xs and not string && y is IEnumerable ys and not string)
        {
            var xl = xs.Cast<object?>().ToList();
            var yl = ys.Cast<object?>().ToList();
            if (xl.Count == yl.Count && xl.Any(i => i is ILoquiObject || i is IEnumerable and not string))
            {
                var any = false;
                for (var i = 0; i < xl.Count; i++)
                {
                    if (Same(xl[i], yl[i])) continue;
                    any = true;
                    Compare($"{name}[{i}]", xl[i], yl[i], depth + 1);
                }
                if (!any) Console.WriteLine($"{pad}{name}: every element equal, yet the lists are not");
                return;
            }
        }
        Console.WriteLine($"{pad}{name}: first  {Show(x)}");
        Console.WriteLine($"{pad}{new string(' ', name.Length)}  second {Show(y)}");
    }

    static bool Same(object? x, object? y)
    {
        if (Equals(x, y)) return true;
        // Lists of different concrete types (List<T> against an overlay list) compare by element.
        if (x is IEnumerable xs and not string && y is IEnumerable ys and not string && x is not ILoquiObject)
        {
            var xl = xs.Cast<object?>().ToList();
            var yl = ys.Cast<object?>().ToList();
            return xl.Count == yl.Count && xl.Zip(yl).All(p => Same(p.First, p.Second));
        }
        return false;
    }
}
