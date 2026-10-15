using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Records;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Masters;

public class SkyrimHardcodedReferenceTests
{
    private static readonly ModKey Skyrim = ModKey.FromFileName("Skyrim.esm");
    private static readonly ModKey Current = ModKey.FromFileName("FixLOD.esp");
    private static readonly ModKey Other = ModKey.FromFileName("Other.esm");

    private static IReadOnlySeparatedMasterPackage Package(GameRelease release, params ModKey[] masters)
    {
        var references = new MasterReferenceCollection(Current);
        references.SetTo(masters.Select(m => new MasterReference { Master = m }));
        return SeparatedMasterPackage.Factory(release, Current, MasterStyle.Small, references, null);
    }

    [Theory]
    [InlineData(GameRelease.SkyrimLE)]
    [InlineData(GameRelease.SkyrimSE)]
    [InlineData(GameRelease.SkyrimSEGog)]
    [InlineData(GameRelease.SkyrimVR)]
    [InlineData(GameRelease.EnderalLE)]
    [InlineData(GameRelease.EnderalSE)]
    [InlineData(GameRelease.EnderalSEGog)]
    public void MasterlessHardcodedReferencesUseGameMaster(GameRelease release)
    {
        var package = Package(release);
        foreach (var id in new uint[] { 1, 0x3B, 0x7FF })
        {
            var expected = new FormKey(Skyrim, id);
            package.GetFormKey(new FormID(id), reference: true).ShouldBe(expected);
            FormKeyBinaryTranslation.Instance.Parse(BitConverter.GetBytes(id), package).ShouldBe(expected);
            package.GetFormID(expected).ShouldBe(new FormID(id));
        }
    }

    [Fact]
    public void HardcodedReferencesDoNotDependOnDeclaredMasterOrder()
    {
        foreach (var package in new[] { Package(GameRelease.SkyrimSE, Other), Package(GameRelease.SkyrimSE, Other, Skyrim) })
        {
            package.GetFormKey(new FormID(0x3B), reference: true).ShouldBe(new FormKey(Skyrim, 0x3B));
            package.GetFormID(new FormKey(Skyrim, 0x3B)).ShouldBe(new FormID(0x3B));
        }
    }

    [Fact]
    public void RecordIdentitiesNullNoneAndOrdinaryLinksKeepTheirMapping()
    {
        var package = Package(GameRelease.SkyrimSE);
        package.GetFormKey(new FormID(0x3B), reference: false).ShouldBe(new FormKey(Current, 0x3B));
        package.GetFormKey(FormID.Null, reference: true).ShouldBe(FormKey.Null);
        package.GetFormKey(FormID.Null, reference: false).ShouldBe(new FormKey(Current, 0));
        package.GetFormID(FormKey.Null).ShouldBe(FormID.Null);
        FormKeyBinaryTranslation.Instance.Parse(BitConverter.GetBytes(uint.MaxValue), package, maxIsNone: true).ShouldBe(FormKey.None);
        package.GetFormKey(new FormID(0x800), reference: true).ShouldBe(new FormKey(Current, 0x800));
        package.GetFormID(new FormKey(Current, 0x800)).ShouldBe(new FormID(0x800));
        var withMaster = Package(GameRelease.SkyrimSE, Other);
        withMaster.GetFormKey(new FormID(0x0100003B), reference: true).ShouldBe(new FormKey(Current, 0x3B));
        withMaster.GetFormKey(new FormID(0x800), reference: true).ShouldBe(new FormKey(Other, 0x800));
    }

    [Theory]
    [InlineData(GameRelease.Oblivion)]
    [InlineData(GameRelease.Fallout3)]
    [InlineData(GameRelease.FalloutNV)]
    [InlineData(GameRelease.Fallout4)]
    public void OtherGamesKeepExistingMapping(GameRelease release)
    {
        Package(release).GetFormKey(new FormID(0x3B), reference: true).ShouldBe(new FormKey(Current, 0x3B));
    }
}
