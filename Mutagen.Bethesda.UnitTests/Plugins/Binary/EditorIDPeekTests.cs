using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

/// <summary>An overlay record's EditorID, read before anything else, is the one its fill would find.</summary>
public class EditorIDPeekTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditorIDsReadBeforeTheFillAreTheRecordsOwn(bool compressed)
    {
        using var folder = TempFolder.Factory();
        var path = Path.Combine(folder.Dir, TestConstants.PluginModKey.FileName);
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var named = mod.Statics.AddNew("Named");
        var unnamed = mod.Statics.AddNew();
        var weapon = mod.Weapons.AddNew("Sword");
        weapon.Name = "Iron Sword";
        foreach (var record in new Mutagen.Bethesda.Plugins.Records.IMajorRecord[] { named, unnamed, weapon }) record.IsCompressed = compressed;
        mod.WriteToBinary(path);

        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        var records = overlay.EnumerateMajorRecords().ToDictionary(r => r.FormKey);
        records[named.FormKey].EditorID.ShouldBe("Named");
        records[unnamed.FormKey].EditorID.ShouldBeNull();
        records[weapon.FormKey].EditorID.ShouldBe("Sword");
        // The rest of the record is still read in full afterwards.
        ((IWeaponGetter)records[weapon.FormKey]).Name?.String.ShouldBe("Iron Sword");
    }
}
