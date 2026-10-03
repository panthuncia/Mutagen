using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim;

/// <summary>
/// An alpha layer's points read from its bytes are the points its data lists: on landscapes of random layers written to
/// a plugin and read back, and on the records as made.
/// </summary>
public class AlphaLayerPointsTests
{
    public static TheoryData<int> Seeds() => [.. Enumerable.Range(1, 20)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Points_are_those_the_data_lists(int seed)
    {
        var random = new Random(seed);
        var mod = new SkyrimMod(ModKey.FromFileName("Land.esp"), SkyrimRelease.SkyrimSE);
        var texture = mod.LandscapeTextures.AddNew();
        var cells = new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = [] };
        mod.Cells.Records.Add(new CellBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock, SubBlocks = [cells] });
        for (var c = 0; c < 6; c++)
        {
            var landscape = new Landscape(mod) { Layers = [] };
            for (var l = random.Next(0, 6); l > 0; l--)
            {
                var alpha = new AlphaLayer { Header = new LayerHeader { Texture = texture.ToLink<ILandscapeTextureGetter>(), Quadrant = (Quadrant)random.Next(4) } };
                if (random.Next(5) != 0)
                {
                    alpha.AlphaLayerData = [];
                    for (var p = random.Next(0, 300); p > 0; p--)
                    {
                        alpha.AlphaLayerData.Add(new AlphaLayerData { Position = (ushort)random.Next(289), Opacity = random.Next(4) == 0 ? 1f : (float)random.NextDouble() });
                    }
                }
                landscape.Layers.Add(alpha);
            }
            cells.Cells.Add(new Cell(mod) { Flags = Cell.Flag.IsInteriorCell, Landscape = landscape });
        }

        using var written = new MemoryStream();
        mod.WriteToBinary(written);
        using var read = SkyrimMod.CreateFromBinaryOverlay(new MemoryStream(written.ToArray()), SkyrimRelease.SkyrimSE, mod.ModKey);

        IEnumerable<IAlphaLayerGetter> Layers(ISkyrimModGetter of) =>
            of.EnumerateMajorRecords<ILandscapeGetter>().SelectMany(l => l.Layers ?? []).OfType<IAlphaLayerGetter>();
        var layers = Layers(read).Concat(Layers(mod)).ToList();
        layers.Count.ShouldBe(2 * Layers(mod).Count());
        foreach (var layer in layers)
        {
            layer.AlphaPoints().ShouldBe((layer.AlphaLayerData ?? []).Select(p => (p.Position, p.Opacity)));
        }
    }
}
