using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

/// <summary>
/// <c>EnumerateMajorRecordBatches</c>, generated for every game: the batches hold exactly the records
/// <c>EnumerateMajorRecords</c> yields, each with the record it is nested in, split at the cell blocks. Records nested
/// twice are in <see cref="Fallout4RecordBatchesTests"/>.
/// </summary>
public class RecordBatchesTests
{
    [Fact]
    public void SkyrimBatchesHoldEveryRecordOnceWithItsParent()
    {
        using var folder = TempFolder.Factory();
        var path = Path.Combine(folder.Dir, TestConstants.PluginModKey.FileName);
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var rock = mod.Statics.AddNew("Rock");
        var topic = mod.DialogTopics.AddNew("Topic");
        var response = new DialogResponses(mod);
        topic.Responses.Add(response);

        var interior = new Cell(mod, "Interior") { Flags = Cell.Flag.IsInteriorCell };
        var interiorRef = new PlacedObject(mod, "InteriorRef");
        interior.Temporary.Add(interiorRef);
        mod.Cells.Records.Add(new CellBlock
        {
            BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks = [new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = [interior] }],
        });
        var world = mod.Worldspaces.AddNew("World");
        var persistent = new Cell(mod);
        var persistentRef = new PlacedObject(mod, "PersistentRef");
        persistent.Persistent.Add(persistentRef);
        world.TopCell = persistent;
        var exterior = new Cell(mod, "Exterior") { Grid = new CellGrid { Point = new P2Int(0, 0) } };
        var exteriorRef = new PlacedObject(mod, "ExteriorRef");
        exterior.Temporary.Add(exteriorRef);
        world.SubCells.Add(new WorldspaceBlock
        {
            BlockNumberX = 0, BlockNumberY = 0, GroupType = GroupTypeEnum.ExteriorCellBlock,
            Items = [new WorldspaceSubBlock { BlockNumberX = 0, BlockNumberY = 0, GroupType = GroupTypeEnum.ExteriorCellSubBlock, Items = [exterior] }],
        });
        mod.WriteToBinary(path);

        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        var batches = overlay.EnumerateMajorRecordBatches().Select(b => b.ToList()).ToList();
        var entries = batches.SelectMany(b => b).ToList();
        entries.Count.ShouldBe(10);
        entries.Select(e => e.Record.FormKey).Order().ShouldBe(overlay.EnumerateMajorRecords().Select(r => r.FormKey).Order());

        var parents = entries.ToDictionary(e => e.Record.FormKey, e => e.Parent?.FormKey);
        parents[rock.FormKey].ShouldBeNull();
        parents[topic.FormKey].ShouldBeNull();
        parents[response.FormKey].ShouldBe(topic.FormKey);
        parents[interior.FormKey].ShouldBeNull();
        parents[interiorRef.FormKey].ShouldBe(interior.FormKey);
        parents[world.FormKey].ShouldBeNull();
        parents[persistent.FormKey].ShouldBe(world.FormKey);
        parents[persistentRef.FormKey].ShouldBe(persistent.FormKey);
        parents[exterior.FormKey].ShouldBe(world.FormKey);
        parents[exteriorRef.FormKey].ShouldBe(exterior.FormKey);

        // The worldspace is a batch with its persistent cell; each block of cells is a batch of its own.
        FormKey[] BatchOf(FormKey formKey) =>
            batches.Single(b => b.Any(e => e.Record.FormKey == formKey)).Select(e => e.Record.FormKey).ToArray();
        BatchOf(world.FormKey).ShouldBe([world.FormKey, persistent.FormKey, persistentRef.FormKey]);
        BatchOf(exterior.FormKey).ShouldBe([exterior.FormKey, exteriorRef.FormKey]);
        BatchOf(interior.FormKey).ShouldBe([interior.FormKey, interiorRef.FormKey]);
    }
}