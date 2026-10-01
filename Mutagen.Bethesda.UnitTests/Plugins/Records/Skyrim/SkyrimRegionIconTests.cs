using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim;

/// <summary>
/// A region whose last data is an icon with nothing after it reads back. Reading it used to look for a subrecord
/// past the icon, beyond the end of the region, which fails when nothing follows the region in the file.
/// </summary>
public class SkyrimRegionIconTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegionEndingInAnIconReadsBack(bool overlay)
    {
        using var folder = TempFolder.Factory();
        var path = Path.Combine(folder.Dir, TestConstants.PluginModKey.FileName);
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var region = mod.Regions.AddNew("IconRegion");
        region.Map = new RegionMap { Icons = new Icons { LargeIconFilename = "Textures\\Region.dds" } };
        mod.WriteToBinary(path);

        using var disposable = overlay ? SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE) : null;
        ISkyrimModGetter read = overlay ? disposable! : SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
        var readRegion = read.Regions.Single();
        readRegion.EditorID.ShouldBe("IconRegion");
        readRegion.Map.ShouldNotBeNull();
        readRegion.Map!.Icons!.LargeIconFilename.DataRelativePath.Path.ShouldBe("Textures\\Region.dds");
    }
}
