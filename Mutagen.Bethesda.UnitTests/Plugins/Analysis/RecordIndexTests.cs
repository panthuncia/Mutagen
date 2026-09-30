using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Mutagen.Bethesda.Testing.AutoData;
using Mutagen.Bethesda.UnitTests.Plugins.Masters;
using Noggog;
using Noggog.IO;
using Shouldly;
using Xunit;
using Fallout4 = Mutagen.Bethesda.Fallout4;
using Oblivion = Mutagen.Bethesda.Oblivion;
using Starfield = Mutagen.Bethesda.Starfield;

namespace Mutagen.Bethesda.UnitTests.Plugins.Analysis;

/// <summary>
/// The record index, read from plugins' bytes, against what Mutagen reads from the same plugins: every record, its
/// EditorID, and its parent (the record whose children group holds it).
/// </summary>
public class RecordIndexTests
{
    private sealed record Written(string Path, SkyrimMod Mod, Worldspace World, Cell Interior, Cell Persistent, Cell Exterior, DialogTopic Topic, Npc Npc);

    private static Written WriteMaster(string folder, bool compressed)
    {
        var mod = new SkyrimMod(TestConstants.MasterModKey, SkyrimRelease.SkyrimSE);
        var npc = mod.Npcs.AddNew("Guard");
        mod.Npcs.AddNew(); // no EditorID
        var interior = new Cell(mod, "Interior") { Flags = Cell.Flag.IsInteriorCell };
        interior.Temporary.Add(new PlacedObject(mod, "InteriorRef"));
        interior.Persistent.Add(new PlacedObject(mod, "PersistentInteriorRef"));
        mod.Cells.Records.Add(new CellBlock
        {
            BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks = [new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = [interior] }],
        });
        var world = mod.Worldspaces.AddNew("World");
        var persistent = new Cell(mod, "WorldPersistent");
        persistent.Persistent.Add(new PlacedObject(mod, "WorldPersistentRef"));
        world.TopCell = persistent;
        var exterior = new Cell(mod, "Exterior") { Grid = new CellGrid { Point = new P2Int(3, -2) } };
        exterior.Temporary.Add(new PlacedObject(mod, "ExteriorRef"));
        exterior.Temporary.Add(new PlacedObject(mod));
        world.SubCells.Add(new WorldspaceBlock
        {
            BlockNumberX = 0, BlockNumberY = -1, GroupType = GroupTypeEnum.ExteriorCellBlock,
            Items = [new WorldspaceSubBlock { BlockNumberX = 0, BlockNumberY = -1, GroupType = GroupTypeEnum.ExteriorCellSubBlock, Items = [exterior] }],
        });
        var topic = mod.DialogTopics.AddNew("Topic");
        topic.Responses.Add(new DialogResponses(mod) { EditorID = "Response" });
        topic.Responses.Add(new DialogResponses(mod));
        // Not worldspaces: before fix/parallel-worldspace-writes, a flagged worldspace was written uncompressed.
        foreach (var record in mod.EnumerateMajorRecords().Where(r => r is Npc or Cell or PlacedObject or DialogResponses)) record.IsCompressed = compressed;
        var path = Path.Combine(folder, mod.ModKey.FileName);
        mod.WriteToBinary(path);
        return new Written(path, mod, world, interior, persistent, exterior, topic, npc);
    }

    /// <summary>Every record as Mutagen reads it, with its EditorID.</summary>
    private static Dictionary<FormKey, string?> Expected(string path, GameRelease release)
    {
        using var mod = ModFactory.ImportGetter(new ModPath(path), release);
        return mod.EnumerateMajorRecords().ToDictionary(r => r.FormKey, r => r.EditorID);
    }

    private static void ShouldMatch(LoadOrderRecordIndex index, int plugin, Dictionary<FormKey, string?> expected)
    {
        var records = index.Plugins[plugin];
        var actual = Enumerable.Range(0, records.Count).ToDictionary(
            r => index.GetFormKey(new RecordVersion(plugin, r)),
            r => records.EditorID(r));
        actual.Keys.ShouldBe(expected.Keys, ignoreOrder: true);
        foreach (var (formKey, editorId) in expected)
        {
            actual[formKey].ShouldBe(string.IsNullOrEmpty(editorId) ? null : editorId, formKey.ToString());
        }
    }

    private static FormKey? ParentOf(LoadOrderRecordIndex index, FormKey formKey)
    {
        index.TryFind(formKey, out var record).ShouldBeTrue();
        return index.Parent(index.Versions(record)[0]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void IndexesEveryRecordWithItsEditorIdAndParent(bool compressed, bool split)
    {
        using var folder = TempFolder.Factory();
        var written = WriteMaster(folder.Dir, compressed);
        var plugin = PluginRecordIndex.FromBytes(File.ReadAllBytes(written.Path), written.Mod.ModKey, GameRelease.SkyrimSE,
            parallel: null, splitThreshold: split ? 0 : int.MaxValue);
        var index = LoadOrderRecordIndex.Create([plugin]);

        ShouldMatch(index, 0, Expected(written.Path, GameRelease.SkyrimSE));
        index.Count.ShouldBe(plugin.Count);
        index.OverriddenCount.ShouldBe(0);
        ParentOf(index, written.Npc.FormKey).ShouldBeNull();
        ParentOf(index, written.Interior.FormKey).ShouldBeNull();
        ParentOf(index, written.Interior.Temporary[0].FormKey).ShouldBe(written.Interior.FormKey);
        ParentOf(index, written.Interior.Persistent[0].FormKey).ShouldBe(written.Interior.FormKey);
        ParentOf(index, written.World.FormKey).ShouldBeNull();
        ParentOf(index, written.Persistent.FormKey).ShouldBe(written.World.FormKey);
        ParentOf(index, written.Persistent.Persistent[0].FormKey).ShouldBe(written.Persistent.FormKey);
        ParentOf(index, written.Exterior.FormKey).ShouldBe(written.World.FormKey);
        ParentOf(index, written.Exterior.Temporary[1].FormKey).ShouldBe(written.Exterior.FormKey);
        ParentOf(index, written.Topic.Responses[0].FormKey).ShouldBe(written.Topic.FormKey);
        plugin.GetRecordType(0).ShouldNotBe(default);
    }

    [Fact]
    public void FromPathMemoryMapsTheSameIndexAsFromBytes()
    {
        using var folder = TempFolder.Factory();
        var written = WriteMaster(folder.Dir, compressed: true);
        var mapped = PluginRecordIndex.FromPath(written.Path, GameRelease.SkyrimSE);
        var read = PluginRecordIndex.FromBytes(File.ReadAllBytes(written.Path), written.Mod.ModKey, GameRelease.SkyrimSE);
        Describe(mapped).ShouldBe(Describe(read));
        mapped.Position(0).ShouldBeGreaterThan(0);
    }

    private static string[] Describe(PluginRecordIndex plugin) => Enumerable.Range(0, plugin.Count)
        .Select(r => $"{plugin.RawFormIDs[r]:X8} {plugin.GetRecordType(r)} {plugin.MajorRecordFlags(r):X} {plugin.Position(r)} {plugin.Parent(r)} {plugin.EditorID(r)}")
        .Prepend($"{plugin.ModKey} {plugin.Release} {plugin.MasterStyle} {string.Join(",", plugin.Masters)}")
        .ToArray();

    [Fact]
    public void GroupsOverridesInLoadOrder()
    {
        using var folder = TempFolder.Factory();
        var written = WriteMaster(folder.Dir, compressed: false);
        var patch = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var renamed = patch.Npcs.GetOrAddAsOverride(written.Npc);
        renamed.EditorID = "GuardRenamed";
        var unnamed = patch.Weapons.AddNew("Sword");
        var patchPath = Path.Combine(folder.Dir, patch.ModKey.FileName);
        patch.WriteToBinary(patchPath);
        var last = new SkyrimMod(TestConstants.PluginModKey2, SkyrimRelease.SkyrimSE);
        last.Npcs.GetOrAddAsOverride(written.Npc).EditorID = null;
        var lastPath = Path.Combine(folder.Dir, last.ModKey.FileName);
        last.WriteToBinary(lastPath);

        var index = LoadOrderRecordIndex.FromPaths([new ModPath(written.Path), new ModPath(patchPath), new ModPath(lastPath)], GameRelease.SkyrimSE);
        ShouldMatch(index, 1, Expected(patchPath, GameRelease.SkyrimSE));
        ShouldMatch(index, 2, Expected(lastPath, GameRelease.SkyrimSE));
        index.OverriddenCount.ShouldBe(1);
        index.Count.ShouldBe(index.Plugins[0].Count + 1);

        index.TryFind(written.Npc.FormKey, out var npc).ShouldBeTrue();
        index.GetFormKey(npc).ShouldBe(written.Npc.FormKey);
        index.Versions(npc).ToArray().Select(v => v.Plugin).ShouldBe([0, 1, 2]);
        index.Winner(npc).Plugin.ShouldBe(2);
        // The last version has no EditorID: the record keeps the one it was last given.
        index.EditorID(npc).ShouldBe("GuardRenamed");
        index.TryFindByEditorID("guardrenamed", out var found).ShouldBeTrue();
        found.ShouldBe(npc);
        index.TryFindByEditorID("Guard", out _).ShouldBeFalse();
        index.TryFindByEditorID("SWORD", out var sword).ShouldBeTrue();
        index.GetFormKey(sword).ShouldBe(unnamed.FormKey);
        index.TryFindByEditorID("EXTERIORREF", out var reference).ShouldBeTrue();
        index.GetFormKey(reference).ShouldBe(written.Exterior.Temporary[0].FormKey);
        index.TryFind(new FormKey(TestConstants.PluginModKey3, 0x800), out _).ShouldBeFalse();
    }

    [Fact]
    public void SavedIndexReadsBackTheSame()
    {
        using var folder = TempFolder.Factory();
        var written = WriteMaster(folder.Dir, compressed: true);
        var plugin = PluginRecordIndex.FromPath(written.Path, GameRelease.SkyrimSE);
        using var stream = new MemoryStream();
        plugin.WriteTo(stream);

        stream.Position = 0;
        PluginRecordIndex.TryReadFrom(stream, out var read).ShouldBeTrue();
        Describe(read!).ShouldBe(Describe(plugin));

        // Anything short of a whole index is refused rather than half read.
        foreach (var length in new[] { 0, 3, 40, (int)stream.Length - 1 })
        {
            PluginRecordIndex.TryReadFrom(new MemoryStream(stream.ToArray()[..length]), out _).ShouldBeFalse(length.ToString());
        }
        var otherVersion = stream.ToArray();
        otherVersion[4]++;
        PluginRecordIndex.TryReadFrom(new MemoryStream(otherVersion), out _).ShouldBeFalse();
    }

    [Fact]
    public void ReadsOblivionHeaders()
    {
        using var folder = TempFolder.Factory();
        var mod = new Oblivion.OblivionMod(TestConstants.MasterModKey, Oblivion.OblivionRelease.Oblivion);
        mod.Npcs.AddNew("OblivionNpc").IsCompressed = true;
        var cell = new Oblivion.Cell(mod, "OblivionCell") { Flags = Oblivion.Cell.Flag.IsInteriorCell };
        cell.Temporary.Add(new Oblivion.PlacedObject(mod, "OblivionRef"));
        mod.Cells.Records.Add(new Oblivion.CellBlock
        {
            BlockNumber = 0, GroupType = Oblivion.GroupTypeEnum.InteriorCellBlock,
            SubBlocks = [new Oblivion.CellSubBlock { BlockNumber = 0, GroupType = Oblivion.GroupTypeEnum.InteriorCellSubBlock, Cells = [cell] }],
        });
        var path = Path.Combine(folder.Dir, mod.ModKey.FileName);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().Write();

        var index = LoadOrderRecordIndex.FromPaths([new ModPath(path)], GameRelease.Oblivion);
        ShouldMatch(index, 0, Expected(path, GameRelease.Oblivion));
        ParentOf(index, cell.Temporary[0].FormKey).ShouldBe(cell.FormKey);
    }

    [Fact]
    public void GivesFallout4QuestChildrenTheirQuest()
    {
        using var folder = TempFolder.Factory();
        var mod = new Fallout4.Fallout4Mod(TestConstants.MasterModKey, Fallout4.Fallout4Release.Fallout4);
        var quest = mod.Quests.AddNew("Quest");
        var topic = new Fallout4.DialogTopic(mod, "QuestTopic");
        topic.Responses.Add(new Fallout4.DialogResponses(mod) { EditorID = "QuestResponse" });
        quest.DialogTopics.Add(topic);
        var path = Path.Combine(folder.Dir, mod.ModKey.FileName);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().Write();

        var index = LoadOrderRecordIndex.FromPaths([new ModPath(path)], GameRelease.Fallout4);
        ShouldMatch(index, 0, Expected(path, GameRelease.Fallout4));
        ParentOf(index, topic.FormKey).ShouldBe(quest.FormKey);
        ParentOf(index, topic.Responses[0].FormKey).ShouldBe(topic.FormKey);
    }

    [Theory, MutagenAutoData]
    public void ResolvesStarfieldMastersByStyle(
        IFileSystem fileSystem,
        DirectoryPath existingDir,
        ModKey originatingKey,
        ModKey fullKey,
        ModKey smallKey,
        ModKey mediumKey)
    {
        var full = new Starfield.StarfieldMod(fullKey, Starfield.StarfieldRelease.Starfield);
        var small = new Starfield.StarfieldMod(smallKey, Starfield.StarfieldRelease.Starfield);
        var medium = new Starfield.StarfieldMod(mediumKey, Starfield.StarfieldRelease.Starfield);
        var originating = new Starfield.StarfieldMod(originatingKey, Starfield.StarfieldRelease.Starfield);
        var overridden = new[] { full.Npcs.AddNew("FullNpc"), small.Npcs.AddNew("SmallNpc"), medium.Npcs.AddNew("MediumNpc") };
        foreach (var npc in overridden) originating.Npcs.GetOrAddAsOverride(npc);
        var own = originating.Npcs.AddNew("OwnNpc");
        var loadOrder = new LoadOrder<IModFlagsGetter>(new[]
        {
            MastersTestUtil.GetFlags(fullKey, MasterStyle.Full),
            MastersTestUtil.GetFlags(smallKey, MasterStyle.Small),
            MastersTestUtil.GetFlags(mediumKey, MasterStyle.Medium),
        });
        var path = Path.Combine(existingDir, originatingKey.FileName);
        originating.BeginWrite.ToPath(path).WithLoadOrder(loadOrder).WithFileSystem(fileSystem).Write();

        var styles = new Cache<IModMasterStyledGetter, ModKey>(m => m.ModKey);
        foreach (var flags in loadOrder.ListedOrder) styles.Set(flags);
        var index = LoadOrderRecordIndex.FromPaths([new ModPath(path)], GameRelease.Starfield, fileSystem, masterFlagsLookup: styles);
        index.Plugins[0].Count.ShouldBe(4);
        Enumerable.Range(0, 4).Select(r => index.GetFormKey(new RecordVersion(0, r)))
            .ShouldBe(overridden.Select(n => n.FormKey).Append(own.FormKey), ignoreOrder: true);
        index.TryFindByEditorID("smallnpc", out var found).ShouldBeTrue();
        index.GetFormKey(found).ShouldBe(overridden[1].FormKey);
    }
}
