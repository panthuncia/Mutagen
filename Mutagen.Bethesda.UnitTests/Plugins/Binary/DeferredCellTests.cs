using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

/// <summary>
/// Skyrim cells read from a plugin defer their own fields until one is read, while their child groups are read when
/// the cell is: interior cells (the generated factory) and worldspace cells (the hand-written one) read the same as
/// the cells written.
/// </summary>
public class DeferredCellTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CellsReadTheSameWhateverIsReadFirst(bool compressed)
    {
        using var folder = TempFolder.Factory();
        var path = Path.Combine(folder.Dir, TestConstants.PluginModKey.FileName);
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var interior = new Cell(mod, "Interior") { Flags = Cell.Flag.IsInteriorCell | Cell.Flag.HasWater, Name = "Inside" };
        interior.Temporary.Add(new PlacedObject(mod, "InteriorRef"));
        interior.Persistent.Add(new PlacedObject(mod, "PersistentRef"));
        mod.Cells.Records.Add(new CellBlock
        {
            BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks = [new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = [interior] }],
        });
        var world = mod.Worldspaces.AddNew("World");
        var exterior = new Cell(mod, "Exterior") { Grid = new CellGrid { Point = new P2Int(3, -2) }, Name = "Outside" };
        exterior.Temporary.Add(new PlacedObject(mod, "ExteriorRef"));
        world.SubCells.Add(new WorldspaceBlock
        {
            BlockNumberX = 0, BlockNumberY = -1, GroupType = GroupTypeEnum.ExteriorCellBlock,
            Items = [new WorldspaceSubBlock { BlockNumberX = 0, BlockNumberY = -1, GroupType = GroupTypeEnum.ExteriorCellSubBlock, Items = [exterior] }],
        });
        foreach (var cell in new[] { interior, exterior }) cell.IsCompressed = compressed;
        mod.WriteToBinary(path);

        using (var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE))
        {
            var cells = overlay.EnumerateMajorRecords<ICellGetter>().ToDictionary(c => c.FormKey);
            // Children first: they are read with the cell, before its own fields.
            cells[interior.FormKey].Temporary.Select(r => r.FormKey).ShouldBe([interior.Temporary[0].FormKey]);
            cells[interior.FormKey].Persistent.Select(r => r.FormKey).ShouldBe([interior.Persistent[0].FormKey]);
            cells[exterior.FormKey].Temporary.Select(r => r.FormKey).ShouldBe([exterior.Temporary[0].FormKey]);
            // Then the EditorID, read before the fill, and the fields the fill reads.
            cells[interior.FormKey].EditorID.ShouldBe("Interior");
            cells[exterior.FormKey].EditorID.ShouldBe("Exterior");
            cells[interior.FormKey].Flags.ShouldBe(Cell.Flag.IsInteriorCell | Cell.Flag.HasWater);
            cells[interior.FormKey].Name!.String.ShouldBe("Inside");
            cells[exterior.FormKey].Grid!.Point.ShouldBe(new P2Int(3, -2));
            cells[exterior.FormKey].Name!.String.ShouldBe("Outside");
        }

        using (var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE))
        {
            // Read the other way round, and compared whole with the cells written.
            var cells = overlay.EnumerateMajorRecords<ICellGetter>().ToDictionary(c => c.FormKey);
            cells[exterior.FormKey].Flags.ShouldBe(exterior.Flags);
            ((ICellGetter)interior).Equals(cells[interior.FormKey]).ShouldBeTrue();
            ((ICellGetter)exterior).Equals(cells[exterior.FormKey]).ShouldBeTrue();
        }
    }
}
