using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

public class ByteArrayListEqualityTests
{
    [Fact]
    public void ListsOfByteArraysCompareByContent()
    {
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var a = mod.MaterialObjects.AddNew("Snow");
        var b = a.Duplicate(a.FormKey);
        a.DNAMs.Add(new byte[] { 1, 2, 3 });
        b.DNAMs.Add(new byte[] { 1, 2, 3 });
        a.Equals(b).ShouldBeTrue();
        b.DNAMs[0] = new byte[] { 1, 2, 4 };
        a.Equals(b).ShouldBeFalse();
    }

    [Fact]
    public void RecordsWithByteArrayListsEqualTheirOverlay()
    {
        using var folder = TempFolder.Factory();
        var path = Path.Combine(folder.Dir, TestConstants.PluginModKey.FileName);
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        mod.MaterialObjects.AddNew("Snow").DNAMs.Add(new byte[] { 1, 2, 3, 4 });
        mod.WriteToBinary(path);

        var first = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE).MaterialObjects.Single();
        var second = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE).MaterialObjects.Single();
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        first.Equals(second).ShouldBeTrue();
        first.Equals(overlay.MaterialObjects.Single()).ShouldBeTrue();
    }
}
