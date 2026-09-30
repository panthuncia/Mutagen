using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

/// <summary>
/// A deferred record's fields read the same as a filled record's, whichever is read first: each member the fill sets
/// completes the fill before it is read.
/// </summary>
public class DeferredFillTests
{
    private static ISkyrimModDisposableGetter RoundTrip(SkyrimMod mod, TempFolder folder)
    {
        var path = Path.Combine(folder.Dir, mod.ModKey.FileName);
        mod.WriteToBinary(path);
        return SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
    }

    [Fact]
    public void GenderedFieldsReadFirstAreFilled()
    {
        using var folder = TempFolder.Factory();
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var association = mod.AssociationTypes.AddNew("Sibling");
        association.Title = new GenderedItem<string?>("Brother", "Sister");
        association.ParentTitle = new GenderedItem<string?>("Father", "Mother");

        using var overlay = RoundTrip(mod, folder);
        var read = overlay.AssociationTypes.Single();
        read.Title!.Male.ShouldBe("Brother");
        read.ParentTitle!.Female.ShouldBe("Mother");
    }

    [Fact]
    public void LoquiFieldsReadFirstAreFilled()
    {
        using var folder = TempFolder.Factory();
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var armor = mod.Armors.AddNew("Cuirass");
        armor.VirtualMachineAdapter = new VirtualMachineAdapter { Scripts = [new ScriptEntry { Name = "ArmorScript" }] };
        armor.WorldModel = new GenderedItem<ArmorModel?>(new ArmorModel { Model = new Model { File = "armor/m.nif" } }, null);

        using var overlay = RoundTrip(mod, folder);
        var read = overlay.Armors.Single();
        read.VirtualMachineAdapter!.Scripts.Single().Name.ShouldBe("ArmorScript");
        read.WorldModel!.Male!.Model!.File.DataRelativePath.Path.ShouldEndWith("m.nif");
    }
}
