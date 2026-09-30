using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

public class RecordBatchesTests
{
    [Fact]
    public void BatchesHoldEveryRecordOnce()
    {
        using var folder = TempFolder.Factory();
        var path = Path.Combine(folder.Dir, TestConstants.PluginModKey.FileName);
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        mod.Statics.AddNew("Rock");
        var topic = mod.DialogTopics.AddNew("Topic");
        topic.Responses.Add(new DialogResponses(mod));

        var interior = new Cell(mod, "Interior") { Flags = Cell.Flag.IsInteriorCell };
        interior.Temporary.Add(new PlacedObject(mod, "InteriorRef"));
        mod.Cells.Records.Add(new CellBlock
        {
            BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks = [new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = [interior] }],
        });
        var world = mod.Worldspaces.AddNew("World");
        var persistent = new Cell(mod);
        persistent.Persistent.Add(new PlacedObject(mod, "PersistentRef"));
        world.TopCell = persistent;
        var exterior = new Cell(mod, "Exterior") { Grid = new CellGrid { Point = new P2Int(0, 0) } };
        exterior.Temporary.Add(new PlacedObject(mod, "ExteriorRef"));
        world.SubCells.Add(new WorldspaceBlock
        {
            BlockNumberX = 0, BlockNumberY = 0, GroupType = GroupTypeEnum.ExteriorCellBlock,
            Items = [new WorldspaceSubBlock { BlockNumberX = 0, BlockNumberY = 0, GroupType = GroupTypeEnum.ExteriorCellSubBlock, Items = [exterior] }],
        });
        mod.WriteToBinary(path);

        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        var batched = overlay.EnumerateMajorRecordBatches().SelectMany(b => b).Select(r => r.FormKey).ToList();
        batched.Count.ShouldBe(10);
        batched.Order().ShouldBe(overlay.EnumerateMajorRecords().Select(r => r.FormKey).Order());
    }
}
