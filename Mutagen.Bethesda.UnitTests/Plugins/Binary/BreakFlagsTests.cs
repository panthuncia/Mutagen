using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

public class BreakFlagsTests
{
    [Fact]
    public void DataStoppingAtABreakSetsThatBreakAndEveryLaterOne()
    {
        using var folder = TempFolder.Factory();
        var path = Path.Combine(folder.Dir, TestConstants.PluginModKey.FileName);
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        // Crime values that stop at the first break, so the fields after the second are absent too.
        mod.Factions.AddNew("Short").CrimeValues = new CrimeValues { Versioning = CrimeValues.VersioningBreaks.Break0 };
        mod.WriteToBinary(path);

        var expected = CrimeValues.VersioningBreaks.Break0 | CrimeValues.VersioningBreaks.Break1;
        SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE).Factions.Single().CrimeValues!.Versioning.ShouldBe(expected);
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        overlay.Factions.Single().CrimeValues!.Versioning.ShouldBe(expected);
    }
}
