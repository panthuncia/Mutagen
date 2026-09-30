using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog;
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
}
