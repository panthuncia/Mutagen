using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

public class GenderedItemEqualityTests
{
    [Fact]
    public void GenderedItemsCompareByValue()
    {
        var a = new GenderedItem<float>(1f, 1.03f);
        var b = new GenderedItem<float>(1f, 1.03f);
        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldNotBe(new GenderedItem<float>(1f, 1f));
    }

    [Fact]
    public void RecordsWithGenderedFieldsCompareByValue()
    {
        using var folder = TempFolder.Factory();
        var path = Path.Combine(folder.Dir, TestConstants.PluginModKey.FileName);
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var addon = mod.ArmorAddons.AddNew("Hat");
        addon.Priority = new GenderedItem<byte>(10, 10);
        addon.WeightSliderEnabled = new GenderedItem<bool>(true, true);
        mod.WriteToBinary(path);

        var first = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE).ArmorAddons.Single();
        var second = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE).ArmorAddons.Single();
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        first.Equals(second).ShouldBeTrue();
        first.Equals(overlay.ArmorAddons.Single()).ShouldBeTrue();
    }

    [Fact]
    public void GenderedItemsOfDifferentDeclaredTypesCompareByValue()
    {
        using var folder = TempFolder.Factory();
        var path = Path.Combine(folder.Dir, TestConstants.PluginModKey.FileName);
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        // The object holds GenderedItem<ArmorModel?>; the overlay an IGenderedItemGetter<IArmorModelGetter?>.
        mod.Armors.AddNew("Cuirass").WorldModel = new GenderedItem<ArmorModel?>(
            new ArmorModel { Model = new Model { File = "armor/m.nif" } },
            new ArmorModel { Model = new Model { File = "armor/f.nif" } });
        mod.WriteToBinary(path);

        var full = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE).Armors.Single();
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        full.Equals(overlay.Armors.Single()).ShouldBeTrue();
        overlay.Armors.Single().Equals(full).ShouldBeTrue();
    }
}
