using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

/// <summary>
/// Fields a record's data stops before (at a break) are not in the file. The full parse leaves them at the field's
/// default, as a new object has them; the overlay should return the same.
/// </summary>
public class OverlayDefaultsTests
{
    private static (ISkyrimModGetter Full, ISkyrimModDisposableGetter Overlay) RoundTrip(SkyrimMod mod, TempFolder folder)
    {
        var path = Path.Combine(folder.Dir, mod.ModKey.FileName);
        mod.WriteToBinary(path);
        return (SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE), SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE));
    }

    [Fact]
    public void FixedLengthBytesPastABreakAreZeros()
    {
        using var folder = TempFolder.Factory();
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        // A DNAM that stops after the material, as 9,720 of Skyrim.esm's statics do.
        mod.Statics.AddNew("Short").DNAMDataTypeState = Static.DNAMDataType.Break0;

        var (full, overlay) = RoundTrip(mod, folder);
        using var _ = overlay;
        full.Statics.Single().Unused.ToArray().ShouldBe(new byte[3]);
        overlay.Statics.Single().Unused.ToArray().ShouldBe(new byte[3]);
    }

    [Fact]
    public void AFieldPastABreakHasItsDeclaredDefault()
    {
        using var folder = TempFolder.Factory();
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        // Script data with no fragments: ExtraBindDataVersion is not written.
        mod.Quests.AddNew("Scripted").VirtualMachineAdapter = new QuestAdapter { Versioning = QuestAdapter.VersioningBreaks.Break0 };

        var (full, overlay) = RoundTrip(mod, folder);
        using var _ = overlay;
        full.Quests.Single().VirtualMachineAdapter!.ExtraBindDataVersion.ShouldBe(QuestAdapter.ExtraBindDataVersionDefault);
        overlay.Quests.Single().VirtualMachineAdapter!.ExtraBindDataVersion.ShouldBe(QuestAdapter.ExtraBindDataVersionDefault);
    }
}
