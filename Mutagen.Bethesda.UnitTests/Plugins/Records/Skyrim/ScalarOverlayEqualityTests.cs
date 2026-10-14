using Xunit;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim;

public sealed class ScalarOverlayEqualityTests
{
    [Fact]
    public void Fixed_list_comparison_never_materializes_elements_and_rejects_incomplete_layouts()
    {
        var bytes = new byte[8 * 4096];
        var left = List(bytes, 8);
        var right = List(bytes.ToArray(), 8);
        static bool Equal(ReadOnlySpan<byte> l, ReadOnlySpan<byte> r, Loqui.Internal.TranslationCrystal? _) => l.SequenceEqual(r);
        for (var i = 0; i < 32; i++) Assert.True(ScalarOverlayEquality.TryEquals(left, right, 8, Equal, null, out _));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var equal = false;
        for (var i = 0; i < 32; i++) ScalarOverlayEquality.TryEquals(left, right, 8, Equal, null, out equal);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(equal);
        Assert.False(ScalarOverlayEquality.TryEquals(left, List(new byte[7], 8), 8, Equal, null, out _));
        Assert.False(ScalarOverlayEquality.TryEquals(left, List(bytes, 4), 8, Equal, null, out _));
        Assert.False(ScalarOverlayEquality.TryEquals(left, Array.Empty<int>(), 8, Equal, null, out _));
        Assert.False(ScalarOverlayEquality.TryEquals(left, null, 8, Equal, null, out _));
        Assert.True(ScalarOverlayEquality.TryEquals(left, List(new byte[8], 8), 8, Equal, null, out equal));
        Assert.False(equal);
    }

    [Fact]
    public void Generated_land_equality_matches_materialized_equality_with_masks_and_float_edge_cases()
    {
        var values = new[] { 0f, -0f, 0.25f, 0.25000003f, float.NaN, float.PositiveInfinity, float.NegativeInfinity };
        foreach (var leftValue in values)
        foreach (var rightValue in values)
        {
            using var left = Read(leftValue, 2, 3);
            using var right = Read(rightValue, 2, 3);
            var l = Layer(left);
            var r = Layer(right);
            var editable = r.DeepCopy();
            Assert.Equal(AlphaLayerMixIn.Equals(l, editable), AlphaLayerMixIn.Equals(l, r));
        }
        using var first = Read(0.5f, 2, 3);
        using var second = Read(0.75f, 4, 5);
        var a = Layer(first);
        var b = Layer(second);
        foreach (var position in new[] { false, true })
        foreach (var unused in new[] { false, true })
        foreach (var opacity in new[] { false, true })
        {
            var mask = new AlphaLayer.TranslationMask(true)
            {
                AlphaLayerData = new AlphaLayerData.TranslationMask(true) { Position = position, Unused = unused, Opacity = opacity },
            };
            Assert.Equal(AlphaLayerMixIn.Equals(a, b.DeepCopy(), mask), AlphaLayerMixIn.Equals(a, b, mask));
        }
    }

    private static BinaryOverlayList.BinaryOverlayListByStartIndex<int> List(byte[] bytes, int size) =>
        new(bytes, null!, (_, _) => throw new InvalidOperationException("Scalar comparison materialized an element."), size);

    private static IAlphaLayerGetter Layer(ISkyrimModGetter mod) =>
        (IAlphaLayerGetter)mod.EnumerateMajorRecords<ILandscapeGetter>().Single().Layers!.Single();

    private static ISkyrimModDisposableGetter Read(float opacity, ushort position, ushort unused)
    {
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("Scalar.esp"), SkyrimRelease.SkyrimSE);
        var landscape = new Landscape(mod)
        {
            Layers = [new AlphaLayer { AlphaLayerData = [new AlphaLayerData { Position = position, Unused = unused, Opacity = opacity }] }],
        };
        mod.Cells.Records.Add(new CellBlock
        {
            BlockNumber = 0,
            GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks = [new CellSubBlock
            {
                BlockNumber = 0,
                GroupType = GroupTypeEnum.InteriorCellSubBlock,
                Cells = [new Cell(mod) { Flags = Cell.Flag.IsInteriorCell, Landscape = landscape }],
            }],
        });
        using var output = new MemoryStream();
        mod.WriteToBinary(output);
        return SkyrimMod.CreateFromBinaryOverlay(new MemoryStream(output.ToArray()), SkyrimRelease.SkyrimSE, mod.ModKey);
    }
}
