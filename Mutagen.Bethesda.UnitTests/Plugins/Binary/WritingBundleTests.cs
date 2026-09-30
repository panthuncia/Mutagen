using System.Buffers.Binary;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

public class WritingBundleTests
{
    private static readonly ModKey Master = ModKey.FromFileName("Master.esm");
    private static readonly ModKey Plugin = ModKey.FromFileName("Plugin.esp");
    private static readonly FormKey MasterRace = new(Master, 0x13746);

    private static SkyrimMod PluginWithMaster()
    {
        var mod = new SkyrimMod(Plugin, SkyrimRelease.SkyrimSE);
        mod.ModHeader.MasterReferences.Add(new MasterReference { Master = Master });
        return mod;
    }

    private static byte[] WriteRecord(IMajorRecordGetter record, WritingBundle bundle)
    {
        using var stream = new MemoryStream();
        using (var writer = new MutagenWriter(stream, bundle, dispose: false))
        {
            ((IBinaryItem)record).WriteToBinary(writer);
        }
        return stream.ToArray();
    }

    private static uint FormIdOf(byte[] record) => BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(12));

    private static uint Subrecord(byte[] record, string type)
    {
        for (var position = 24; position < record.Length;)
        {
            var length = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(position + 4));
            if (Encoding.ASCII.GetString(record, position, 4) == type) return BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(position + 6));
            position += 6 + length;
        }
        throw new InvalidOperationException($"No {type} subrecord.");
    }

    [Fact]
    public void A_mods_own_record_is_written_with_the_mods_own_index_and_links_with_their_masters_index()
    {
        var mod = PluginWithMaster();
        var npc = mod.Npcs.AddNew();
        npc.Race.SetTo(MasterRace);

        var bytes = WriteRecord(npc, WritingBundle.ForRecordsOf(mod));

        FormIdOf(bytes).ShouldBe(0x01000000u | npc.FormKey.ID);
        Subrecord(bytes, "RNAM").ShouldBe(MasterRace.ID);
    }

    [Fact]
    public void An_override_is_written_with_its_masters_index()
    {
        var mod = PluginWithMaster();
        var npc = new Npc(new FormKey(Master, 0x800), SkyrimRelease.SkyrimSE);

        var bytes = WriteRecord(npc, WritingBundle.ForRecordsOf(mod));

        FormIdOf(bytes).ShouldBe(0x800u);
    }

    [Fact]
    public void A_single_record_is_written_as_writing_the_whole_mod_writes_it()
    {
        var mod = PluginWithMaster();
        var npc = mod.Npcs.AddNew("SingleRecord");
        npc.Race.SetTo(MasterRace);
        using var whole = new MemoryStream();
        mod.WriteToBinary(whole, new BinaryWriteParameters { MastersListOrdering = new MastersListOrderingByLoadOrder([Master, Plugin]) });

        var single = WriteRecord(npc, WritingBundle.ForRecordsOf(mod));

        var written = whole.ToArray();
        var at = written.AsSpan().IndexOf(single.AsSpan(0, 16));
        at.ShouldBeGreaterThan(0);
        written.AsSpan(at, single.Length).ToArray().ShouldBe(single);
    }

    [Fact]
    public void Writing_a_record_without_the_mods_masters_says_how_to_provide_them()
    {
        var npc = PluginWithMaster().Npcs.AddNew();
        npc.Race.SetTo(MasterRace);

        var error = Should.Throw<Exception>(() => WriteRecord(npc, new WritingBundle(GameConstants.SkyrimSE)));

        error.ToString().ShouldContain(nameof(WritingBundle.ForRecordsOf));
    }
}
