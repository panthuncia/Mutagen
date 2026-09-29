using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

public class NullableEnumFallbackTests
{
    [Fact]
    public void NullWrittenAsItsFallbackReadsBackAsNull()
    {
        using var tempFolder = TempFolder.Factory();
        var path = Path.Combine(tempFolder.Dir, TestConstants.PluginModKey.FileName);
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        // Skill is written as -1 when null (nullableBinaryFallback="-1").
        mod.Weapons.AddNew("Staff").Data = new WeaponData { Skill = null };
        mod.WriteToBinary(path);

        var full = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        full.Weapons.Single().Data!.Skill.ShouldBeNull();
        overlay.Weapons.Single().Data!.Skill.ShouldBeNull();
    }
}
