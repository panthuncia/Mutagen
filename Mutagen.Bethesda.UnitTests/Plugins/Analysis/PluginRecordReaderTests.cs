using Loqui;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Testing;
using Noggog;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Analysis;

/// <summary>
/// A record read alone, at its position in the file, is the record an overlay of the whole plugin reads, apart from
/// the records a cell, worldspace or topic holds, which it doesn't read.
/// </summary>
public class PluginRecordReaderTests
{
    private static string Write(string folder, bool compressed, bool localized)
    {
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE) { UsingLocalization = localized };
        var npc = mod.Npcs.AddNew("Guard");
        npc.Name = "Town Guard";
        mod.Npcs.AddNew();
        // An owner is read by the type of the record it names, which the plugin's other records tell.
        var chest = mod.Containers.AddNew("Chest");
        chest.Items = [new ContainerEntry
        {
            Item = new ContainerItem { Item = mod.MiscItems.AddNew("Gem").ToLink<IItemGetter>(), Count = 2 },
            Data = new ExtraData { Owner = new NpcOwner { Npc = npc.ToLink<INpcGetter>() }, ItemCondition = 1 },
        }];
        var interior = new Cell(mod, "Interior") { Flags = Cell.Flag.IsInteriorCell, Name = "Inside" };
        interior.Temporary.Add(new PlacedObject(mod, "InteriorRef"));
        interior.Persistent.Add(new PlacedNpc(mod, "PersistentNpc") { Base = npc.ToNullableLink<INpcGetter>() });
        mod.Cells.Records.Add(new CellBlock
        {
            BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks = [new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = [interior] }],
        });
        var world = mod.Worldspaces.AddNew("World");
        var exterior = new Cell(mod, "Exterior") { Grid = new CellGrid { Point = new P2Int(3, -2) } };
        exterior.Temporary.Add(new PlacedObject(mod, "ExteriorRef"));
        world.SubCells.Add(new WorldspaceBlock
        {
            BlockNumberX = 0, BlockNumberY = -1, GroupType = GroupTypeEnum.ExteriorCellBlock,
            Items = [new WorldspaceSubBlock { BlockNumberX = 0, BlockNumberY = -1, GroupType = GroupTypeEnum.ExteriorCellSubBlock, Items = [exterior] }],
        });
        var topic = mod.DialogTopics.AddNew("Topic");
        topic.Responses.Add(new DialogResponses(mod) { EditorID = "Response" });
        foreach (var record in mod.EnumerateMajorRecords().Where(r => r is not Worldspace)) record.IsCompressed = compressed;
        var path = Path.Combine(folder, mod.ModKey.FileName);
        mod.WriteToBinary(path);
        return path;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ReadsEachRecordAsTheWholePluginReadsIt(bool compressed, bool localized)
    {
        using var folder = TempFolder.Factory();
        var path = Write(folder.Dir, compressed, localized);
        var locations = RecordLocator.GetLocations(new ModPath(path), GameRelease.SkyrimSE, loadOrder: null);
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        using var reader = PluginRecordReader.FromPath(new ModPath(path), GameRelease.SkyrimSE);

        var records = overlay.EnumerateMajorRecords().ToArray();
        records.Length.ShouldBe(12);
        foreach (var expected in records)
        {
            var getterType = ((ILoquiObject)expected).Registration.GetterType;
            var read = reader.Read(locations[expected.FormKey].Min, getterType);
            read.FormKey.ShouldBe(expected.FormKey);
            read.EditorID.ShouldBe(expected.EditorID);
            getterType.IsInstanceOfType(read).ShouldBeTrue();
            switch (read)
            {
                case ICellGetter cell:
                    cell.Temporary.ShouldBeEmpty();
                    cell.Persistent.ShouldBeEmpty();
                    cell.Flags.ShouldBe(((ICellGetter)expected).Flags);
                    cell.Grid?.Point.ShouldBe(((ICellGetter)expected).Grid!.Point);
                    cell.Name?.String.ShouldBe(((ICellGetter)expected).Name?.String);
                    break;
                case IWorldspaceGetter world:
                    world.SubCells.ShouldBeEmpty();
                    break;
                case IDialogTopicGetter topic:
                    topic.Responses.ShouldBeEmpty();
                    break;
                default:
                    read.Equals(expected).ShouldBeTrue(expected.ToString());
                    break;
            }
        }
        reader.Read<INpcGetter>(locations[records.OfType<INpcGetter>().First(n => n.EditorID == "Guard").FormKey].Min)
            .Name!.String.ShouldBe("Town Guard");
        var chest = reader.Read<IContainerGetter>(locations[records.OfType<IContainerGetter>().Single().FormKey].Min);
        chest.Items![0].Data!.Owner.ShouldBeOfType<NpcOwner>();
    }

    [Fact]
    public void ReadsFromSeveralThreads()
    {
        using var folder = TempFolder.Factory();
        var path = Write(folder.Dir, compressed: true, localized: false);
        var locations = RecordLocator.GetLocations(new ModPath(path), GameRelease.SkyrimSE, loadOrder: null);
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        using var reader = PluginRecordReader.FromPath(new ModPath(path), GameRelease.SkyrimSE);
        var placed = overlay.EnumerateMajorRecords<IPlacedObjectGetter>().ToArray();

        Parallel.For(0, 2000, i =>
        {
            var expected = placed[i % placed.Length];
            reader.Read<IPlacedObjectGetter>(locations[expected.FormKey].Min).Equals(expected).ShouldBeTrue();
        });
    }

    [Fact]
    public void ReadsWhatWasThereWhileThePluginIsReplaced()
    {
        // As an editor saves: the plugin moved aside, a new one put in its place, and the old one deleted.
        using var folder = TempFolder.Factory();
        var path = Write(folder.Dir, compressed: false, localized: false);
        var locations = RecordLocator.GetLocations(new ModPath(path), GameRelease.SkyrimSE, loadOrder: null);
        FormKey guard;
        using (var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE))
        {
            guard = overlay.Npcs.First(n => n.EditorID == "Guard").FormKey;
        }
        using var reader = PluginRecordReader.FromPath(new ModPath(path), GameRelease.SkyrimSE);

        File.Move(path, path + ".bak");
        File.WriteAllBytes(path, [1, 2, 3]);
        File.Delete(path + ".bak");

        reader.Read<INpcGetter>(locations[guard].Min).Name!.String.ShouldBe("Town Guard");
    }

    [Fact]
    public void RefusesAGroup()
    {
        using var folder = TempFolder.Factory();
        var path = Write(folder.Dir, compressed: false, localized: false);
        using var reader = PluginRecordReader.FromPath(new ModPath(path), GameRelease.SkyrimSE);
        var tes4Length = 24 + BitConverter.ToInt32(File.ReadAllBytes(path), 4);

        Should.Throw<ArgumentException>(() => reader.Read<INpcGetter>(tes4Length));
    }
}
