using Loqui;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog;
using Shouldly;
using Xunit;
using static Loqui.EqualsMaskHelper;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

/// <summary>
/// <see cref="EqualsMasks"/> builds the same masks as Loqui's <see cref="EqualsMaskHelper"/>, which generated code
/// used before: the same overall result, and the same entries in the same order, in both include modes, except where
/// Loqui's 2D arrays report equal elements as failures.
/// </summary>
public class EqualsMasksTests
{
    public static TheoryData<int[]?, int[]?> ValueLists => new()
    {
        { null, null }, { null, [1] }, { [1], null }, { [], [] },
        { [1, 2, 3], [1, 2, 3] }, { [1, 2, 3], [1, 9, 3] }, { [1, 2], [1, 2, 3] }, { [1, 2, 3], [1, 2] }, { [1, 2, 3], [7, 2] },
    };

    [Theory]
    [MemberData(nameof(ValueLists))]
    public void ValueListsMatchLoqui(int[]? lhs, int[]? rhs)
    {
        foreach (var include in new[] { Include.All, Include.OnlyFailures })
        {
            // As lists, which know their counts, and as enumerables, which do not.
            Same(lhs.ListEqualsMask(rhs, (l, r) => l == r, include), ((IEnumerable<int>?)lhs).CollectionEqualsHelper(rhs, (l, r) => l == r, include));
            Same(Lazy(lhs).ListEqualsMask(Lazy(rhs), (l, r) => l == r, include), Lazy(lhs).CollectionEqualsHelper(Lazy(rhs), (l, r) => l == r, include));
        }
    }

    [Theory]
    [MemberData(nameof(ValueLists))]
    public void MaskListsMatchLoqui(int[]? lhs, int[]? rhs)
    {
        var left = Entries(lhs);
        var right = Entries(rhs);
        foreach (var include in new[] { Include.All, Include.OnlyFailures })
        {
            Same(left.ListEqualsMask(right, (l, r, i) => l.GetEqualsMask(r, i), include),
                ((IEnumerable<LeveledItemEntry>?)left).CollectionEqualsHelper(right, (l, r) => l.GetEqualsMask(r, include), include));
        }
    }

    public static TheoryData<int[,]?, int[,]?> ValueArrays => new()
    {
        { null, null }, { null, new int[1, 1] }, { new int[2, 3], new int[3, 2] },
        { new[,] { { 1, 2, 3 }, { 4, 5, 6 } }, new[,] { { 1, 2, 3 }, { 4, 5, 6 } } },
        { new[,] { { 1, 2, 3 }, { 4, 5, 6 } }, new[,] { { 1, 2, 3 }, { 4, 0, 6 } } },
    };

    [Theory]
    [MemberData(nameof(ValueArrays))]
    public void ValueArraysMatchLoqui(int[,]? lhs, int[,]? rhs)
    {
        var left = Array2d(lhs, v => v);
        var right = Array2d(rhs, v => v);
        var all = left.Array2dEqualsMask(right, (l, r) => l == r, Include.All);
        Same(all, left.Array2dEqualsHelper(right, (l, r) => l == r, Include.All));
        // Loqui's 2D arrays call any equal element a failure in this mode (so equal arrays compare unequal); here only
        // the unequal elements are, as for lists.
        var failures = left.Array2dEqualsMask(right, (l, r) => l == r, Include.OnlyFailures);
        if (all!.Overall) failures.ShouldBeNull();
        else Same(failures, new MaskItem<bool, IEnumerable<(P2Int Index, bool EqualValues)>?>(false, all.Specific?.Where(e => !e.EqualValues).ToList()));
    }

    [Theory]
    [MemberData(nameof(ValueArrays))]
    public void MaskArraysMatchLoqui(int[,]? lhs, int[,]? rhs)
    {
        var left = Array2d(lhs, Entry);
        var right = Array2d(rhs, Entry);
        var all = left.Array2dEqualsMask(right, (l, r, i) => l.GetEqualsMask(r, i), Include.All);
        Same(all, left.Array2dEqualsHelper(right, (l, r) => l.GetEqualsMask(r, Include.All), Include.All));
        var failures = left.Array2dEqualsMask(right, (l, r, i) => l.GetEqualsMask(r, i), Include.OnlyFailures);
        if (all!.Overall) failures.ShouldBeNull();
        else
        {
            failures.ShouldNotBeNull();
            failures.Overall.ShouldBeFalse();
            if (all.Specific == null) failures.Specific.ShouldBeNull();
            else failures.Specific!.Select(e => e.Index).ShouldBe(all.Specific.Where(e => !e.Overall).Select(e => e.Index));
        }
    }

    [Fact]
    public void RecordMasksCompareTheirLists()
    {
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var list = mod.LeveledItems.AddNew("List");
        list.Entries = [.. Entries([1, 2, 3])!];
        var other = (LeveledItem)list.DeepCopy();
        list.GetEqualsMask(other).Entries!.Overall.ShouldBeTrue();
        other.Entries![1].Data!.Count = 9;
        var mask = list.GetEqualsMask(other);
        mask.Entries!.Overall.ShouldBeFalse();
        mask.Entries.Specific!.Select(e => e.Overall).ShouldBe([true, false, true]);
    }

    private static IEnumerable<int>? Lazy(int[]? values) => values == null ? null : values.Select(v => v);

    private static List<LeveledItemEntry>? Entries(int[]? counts) => counts?.Select(Entry).ToList();

    private static LeveledItemEntry Entry(int count) => new() { Data = new LeveledItemEntryData { Count = (short)count, Level = 1 } };

    private static IReadOnlyArray2d<T>? Array2d<T>(int[,]? values, Func<int, T> make)
    {
        if (values == null) return null;
        // Rows of the literal are y, columns x.
        var cells = new T[values.GetLength(1), values.GetLength(0)];
        for (var y = 0; y < values.GetLength(0); y++)
        {
            for (var x = 0; x < values.GetLength(1); x++) cells[x, y] = make(values[y, x]);
        }
        return new Array2d<T>(cells);
    }

    private static void Same<TSpecific>(MaskItem<bool, IEnumerable<TSpecific>?>? actual, MaskItem<bool, IEnumerable<TSpecific>?>? expected)
    {
        if (expected == null)
        {
            actual.ShouldBeNull();
            return;
        }
        actual.ShouldNotBeNull();
        actual.Overall.ShouldBe(expected.Overall);
        if (expected.Specific == null)
        {
            actual.Specific.ShouldBeNull();
            return;
        }
        actual.Specific.ShouldNotBeNull();
        actual.Specific.ShouldBe(expected.Specific);
    }
}