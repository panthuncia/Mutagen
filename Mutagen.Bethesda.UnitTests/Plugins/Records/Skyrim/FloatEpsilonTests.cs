using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim;

public class FloatEpsilonTests
{
    // Files store some zeros as float.Epsilon; the full parse reads them as zero, and the overlay must too.
    [Fact]
    public void EpsilonReadsAsZeroThroughBothPaths()
    {
        using var folder = TempFolder.Factory();
        var path = Path.Combine(folder.Dir, TestConstants.PluginModKey.FileName);
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        const float marker = 12345.678f;
        mod.Explosions.AddNew("Explosion").ISRadius = marker;
        mod.MagicEffects.AddNew("Effect").Conditions.Add(new ConditionFloat
        {
            ComparisonValue = marker,
            Data = new GetIsIDConditionData(),
        });
        mod.WriteToBinary(path);
        var bytes = File.ReadAllBytes(path);
        var pattern = BitConverter.GetBytes(marker);
        var replaced = 0;
        for (var i = 0; i + 4 <= bytes.Length; i++)
        {
            if (!bytes.AsSpan(i, 4).SequenceEqual(pattern)) continue;
            BitConverter.GetBytes(float.Epsilon).CopyTo(bytes, i);
            replaced++;
        }
        replaced.ShouldBe(2);
        File.WriteAllBytes(path, bytes);

        var full = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        full.Explosions.Single().ISRadius.ShouldBe(0f);
        overlay.Explosions.Single().ISRadius.ShouldBe(0f);
        ((IConditionFloatGetter)overlay.MagicEffects.Single().Conditions[0]).ComparisonValue.ShouldBe(0f);
        full.Explosions.Single().GetHashCode().ShouldBe(overlay.Explosions.Single().GetHashCode());
    }
}
