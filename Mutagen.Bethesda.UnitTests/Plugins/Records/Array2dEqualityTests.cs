using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

/// <summary><see cref="Array2dEquality"/> agrees with comparing the arrays as sequences, which generated Equals used before.</summary>
public class Array2dEqualityTests
{
    public static TheoryData<int[,]?, int[,]?> Arrays => new()
    {
        { null, null }, { null, new int[1, 1] }, { new int[1, 1], null }, { new int[2, 3], new int[3, 2] },
        { new[,] { { 1, 2, 3 }, { 4, 5, 6 } }, new[,] { { 1, 2, 3 }, { 4, 5, 6 } } },
        { new[,] { { 1, 2, 3 }, { 4, 5, 6 } }, new[,] { { 1, 2, 3 }, { 4, 0, 6 } } },
    };

    [Theory]
    [MemberData(nameof(Arrays))]
    public void MatchesSequenceComparison(int[,]? lhs, int[,]? rhs)
    {
        var left = lhs == null ? null : new Array2d<int>(lhs);
        var right = rhs == null ? null : new Array2d<int>(rhs);
        left.Array2dEquals(right).ShouldBe(((IReadOnlyArray2d<int>?)left).SequenceEqualNullable(right));
    }

    [Fact]
    public void LandscapesCompareTheirArrays()
    {
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var landscape = new Landscape(mod)
        {
            VertexNormals = new Array2d<P3UInt8>(33, 33, new P3UInt8(1, 2, 3)),
            VertexColors = new Array2d<P3UInt8>(33, 33, new P3UInt8(4, 5, 6)),
        };
        var copy = (Landscape)landscape.DeepCopy();
        landscape.Equals(copy).ShouldBeTrue();
        copy.VertexNormals![32, 32] = new P3UInt8(9, 9, 9);
        landscape.Equals(copy).ShouldBeFalse();
    }
    [Theory]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(32, 32)]
    [InlineData(16, 3)]
    public void ArraysReadFromPluginsCompareByTheirBytes(int x, int y)
    {
        // Two plugins' landscapes, read back as overlays: equal, or different in one element.
        using var folder = TempFolder.Factory();
        var overlays = new List<IDisposable>();
        ILandscapeGetter Read(string name, bool change)
        {
            var modKey = ModKey.FromFileName(name);
            var mod = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);
            var normals = new Array2d<P3UInt8>(33, 33, new P3UInt8(1, 2, 3));
            if (change && x >= 0) normals[x, y] = new P3UInt8(7, 8, 9);
            var cell = new Cell(mod, "Cell") { Flags = Cell.Flag.IsInteriorCell, Landscape = new Landscape(mod) { VertexNormals = normals } };
            mod.Cells.Records.Add(new CellBlock
            {
                BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock,
                SubBlocks = [new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = [cell] }],
            });
            var path = Path.Combine(folder.Dir, name);
            mod.WriteToBinary(path);
            var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
            overlays.Add(overlay);
            return overlay.EnumerateMajorRecords<ILandscapeGetter>().Single();
        }
        var left = Read("Left.esp", change: false).VertexNormals;
        var right = Read("Right.esp", change: true).VertexNormals;
        var elementwise = left.Array2dEquals(right, (l, r) => l.Equals(r));
        left.Array2dEquals(right).ShouldBe(elementwise);
        elementwise.ShouldBe(x < 0);
        overlays.ForEach(o => o.Dispose());
    }
}
