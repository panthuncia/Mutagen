using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class ConditionPackDataFlagTests
{
    // A condition's "use pack data" bit belongs on its data (UsePackageData), not in Flags, for both readers.
    [Fact]
    public void PackDataFlagIsReadIntoData()
    {
        using var folder = TempFolder.Factory();
        var path = Path.Combine(folder.Dir, TestConstants.PluginModKey.FileName);
        var mod = new StarfieldMod(TestConstants.PluginModKey, StarfieldRelease.Starfield);
        mod.MagicEffects.AddNew("Effect").Conditions.Add(new ConditionFloat
        {
            Flags = Condition.Flag.OR,
            ComparisonValue = 1,
            Data = new GetIsIDConditionData { UsePackageData = true },
        });
        mod.WriteToBinary(path);

        var full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield).MagicEffects.Single();
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
        var read = overlay.MagicEffects.Single();
        full.Conditions[0].Flags.ShouldBe(Condition.Flag.OR);
        read.Conditions[0].Flags.ShouldBe(Condition.Flag.OR);
        read.Conditions[0].Data.UsePackageData.ShouldBeTrue();
        full.Equals(read).ShouldBeTrue();
    }
}
