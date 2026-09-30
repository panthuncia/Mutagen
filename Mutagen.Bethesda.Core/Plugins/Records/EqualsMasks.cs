using Loqui;
using Noggog;
using static Loqui.EqualsMaskHelper;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// The equals masks of lists and 2D arrays, as Loqui's <see cref="EqualsMaskHelper"/> builds them (the same overall
/// result and the same entries), allocating little beyond the result: entries go into an array sized once rather than
/// a list grown element by element and copied, and an element's mask is made with <c>include</c> passed in rather than
/// captured, so the generated comparer needs no closure. Generated <c>GetEqualsMask</c> calls these; comparing records
/// field by field (conflict detection) spends most of its time here.
/// </summary>
public static class EqualsMasks
{
    private static readonly Func<bool, bool> IsTrue = static b => b;

    /// <summary>A list's mask from a comparison of each pair of elements.</summary>
    public static MaskItem<bool, IEnumerable<(int Index, bool EqualValues)>?>? ListEqualsMask<T>(
        this IEnumerable<T>? lhs,
        IEnumerable<T>? rhs,
        Func<T, T, bool> equals,
        Include include)
    {
        if (lhs == null || rhs == null)
        {
            if (lhs != null || rhs != null) return new(false, null);
            return include == Include.All ? new(true, null) : null;
        }
        var all = include == Include.All;
        var entries = all ? new List<(int Index, bool EqualValues)>(Capacity(lhs, rhs)) : null;
        List<(int Index, bool EqualValues)>? failures = null;
        var equal = true;
        using var left = lhs.GetEnumerator();
        using var right = rhs.GetEnumerator();
        var index = 0;
        while (left.MoveNext())
        {
            if (!right.MoveNext())
            {
                equal = false;
                break;
            }
            var same = equals(left.Current, right.Current);
            if (all) entries!.Add((index, same));
            else if (!same) (failures ??= []).Add((index, same));
            if (!same) equal = false;
            index++;
        }
        if (equal && right.MoveNext()) equal = false;
        if (all) return new(equal, entries);
        return equal ? null : new(false, failures ?? []);
    }

    /// <summary>A list's mask from each pair of elements' own masks.</summary>
    public static MaskItem<bool, IEnumerable<MaskItemIndexed<bool, M?>>?>? ListEqualsMask<T, M>(
        this IEnumerable<T>? lhs,
        IEnumerable<T>? rhs,
        Func<T, T, Include, M> getMask,
        Include include)
        where M : class, IMask<bool>
    {
        if (lhs == null || rhs == null)
        {
            if (lhs != null || rhs != null) return new(false, null);
            return include == Include.All ? new(true, null) : null;
        }
        var all = include == Include.All;
        var entries = new List<MaskItemIndexed<bool, M?>>(all ? Capacity(lhs, rhs) : 0);
        var equal = true;
        using var left = lhs.GetEnumerator();
        using var right = rhs.GetEnumerator();
        var index = 0;
        while (left.MoveNext())
        {
            if (!right.MoveNext())
            {
                equal = false;
                break;
            }
            var entry = Entry(new MaskItemIndexed<bool, M?>(index++, false, null), left.Current, right.Current, getMask, include);
            if (!entry.Overall) equal = false;
            if (all || !entry.Overall) entries.Add(entry);
        }
        if (equal && right.MoveNext()) equal = false;
        if (all) return new(equal, entries);
        return equal ? null : new(false, entries);
    }

    /// <summary>A 2D array's mask from a comparison of each pair of elements, row by row.</summary>
    public static MaskItem<bool, IEnumerable<(P2Int Index, bool EqualValues)>?>? Array2dEqualsMask<T>(
        this IReadOnlyArray2d<T>? lhs,
        IReadOnlyArray2d<T>? rhs,
        Func<T, T, bool> equals,
        Include include)
    {
        if (lhs == null || rhs == null)
        {
            if (lhs != null || rhs != null) return new(false, null);
            return include == Include.All ? new(true, null) : null;
        }
        if (lhs.Width != rhs.Width || lhs.Height != rhs.Height) return new(false, null);
        var all = include == Include.All;
        var entries = all ? new (P2Int Index, bool EqualValues)[lhs.Width * lhs.Height] : null;
        List<(P2Int Index, bool EqualValues)>? failures = null;
        var equal = true;
        var i = 0;
        for (var y = 0; y < lhs.Height; y++)
        {
            for (var x = 0; x < lhs.Width; x++)
            {
                var same = equals(lhs[x, y], rhs[x, y]);
                if (!same)
                {
                    equal = false;
                    if (!all) (failures ??= []).Add((new P2Int(x, y), false));
                }
                if (all) entries![i++] = (new P2Int(x, y), same);
            }
        }
        if (all) return new(equal, entries);
        return equal ? null : new(false, failures);
    }

    /// <summary>A 2D array's mask from each pair of elements' own masks, row by row.</summary>
    public static MaskItem<bool, IEnumerable<MaskItemIndexed<P2Int, bool, M?>>?>? Array2dEqualsMask<T, M>(
        this IReadOnlyArray2d<T>? lhs,
        IReadOnlyArray2d<T>? rhs,
        Func<T, T, Include, M> getMask,
        Include include)
        where M : class, IMask<bool>
    {
        if (lhs == null || rhs == null)
        {
            if (lhs != null || rhs != null) return new(false, null);
            return include == Include.All ? new(true, null) : null;
        }
        if (lhs.Width != rhs.Width || lhs.Height != rhs.Height) return new(false, null);
        var all = include == Include.All;
        var entries = new List<MaskItemIndexed<P2Int, bool, M?>>(all ? lhs.Width * lhs.Height : 0);
        var equal = true;
        for (var y = 0; y < lhs.Height; y++)
        {
            for (var x = 0; x < lhs.Width; x++)
            {
                var entry = Entry(new MaskItemIndexed<P2Int, bool, M?>(new P2Int(x, y), false, null), lhs[x, y], rhs[x, y], getMask, include);
                if (!entry.Overall) equal = false;
                if (all || !entry.Overall) entries.Add(entry);
            }
        }
        if (all) return new(equal, entries);
        return equal ? null : new(false, entries);
    }

    /// <summary>One element's entry. An element missing on both sides is equal, on one side not.</summary>
    private static TEntry Entry<TEntry, T, M>(TEntry entry, T lhs, T rhs, Func<T, T, Include, M> getMask, Include include)
        where TEntry : MaskItem<bool, M?>
        where M : class, IMask<bool>
    {
        if (lhs == null || rhs == null)
        {
            entry.Overall = lhs == null && rhs == null;
            return entry;
        }
        var mask = getMask(lhs, rhs, include);
        entry.Overall = mask.All(IsTrue);
        if (!entry.Overall || include == Include.All) entry.Specific = mask;
        return entry;
    }

    /// <summary>The entries a list's mask will hold, when both lists know their counts (overlay lists are read-only collections).</summary>
    private static int Capacity<T>(IEnumerable<T> lhs, IEnumerable<T> rhs) =>
        Count(lhs) is { } left && Count(rhs) is { } right ? Math.Min(left, right) : 0;

    private static int? Count<T>(IEnumerable<T> items) => items switch
    {
        IReadOnlyCollection<T> collection => collection.Count,
        _ when items.TryGetNonEnumeratedCount(out var count) => count,
        _ => null,
    };
}