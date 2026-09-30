using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

public class ContentHashCodeTests
{
    // Records equal by Equals must hash alike, whatever lists and byte arrays they hold and however they were read.
    [Fact]
    public void EqualRecordsHashAlike()
    {
        using var folder = TempFolder.Factory();
        var path = Path.Combine(folder.Dir, TestConstants.PluginModKey.FileName);
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var faction = mod.Factions.AddNew("Faction");
        var npc = mod.Npcs.AddNew("Npc");
        npc.Factions.Add(new RankPlacement { Faction = faction.ToLink(), Rank = 1 });
        npc.Keywords = [FormKey.Factory("000001:Skyrim.esm").ToLink<IKeywordGetter>()];
        var spell = mod.Spells.AddNew("Spell");
        spell.Effects.Add(new Effect
        {
            BaseEffect = FormKey.Factory("000002:Skyrim.esm").ToNullableLink<IMagicEffectGetter>(),
            Conditions = [new ConditionFloat { ComparisonValue = 1, Data = new GetIsIDConditionData() }],
        });
        mod.MaterialObjects.AddNew("Material").DNAMs.Add(new byte[] { 1, 2, 3 });
        mod.WriteToBinary(path);

        var first = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
        var second = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        IMajorRecordGetter[] Records(ISkyrimModGetter m) => [m.Npcs.Single(), m.Spells.Single(), m.MaterialObjects.Single()];
        foreach (var (a, b, c) in Records(first).Zip(Records(second), Records(overlay)))
        {
            a.Equals(b).ShouldBeTrue();
            a.GetHashCode().ShouldBe(b.GetHashCode());
            a.Equals(c).ShouldBeTrue();
            a.GetHashCode().ShouldBe(c.GetHashCode());
        }
        first.Npcs.Single().Factions[0].GetHashCode().ShouldBe(second.Npcs.Single().Factions[0].GetHashCode());
    }

    [Fact]
    public void ContentsAreHashedInOrder()
    {
        var a = new HashCode();
        a.AddContents(new[] { 1, 2 });
        var b = new HashCode();
        b.AddContents(new[] { 2, 1 });
        a.ToHashCode().ShouldNotBe(b.ToHashCode());

        var x = new HashCode();
        x.AddContents(new Dictionary<int, byte> { [1] = 1, [2] = 2 });
        var y = new HashCode();
        y.AddContents(new Dictionary<int, byte> { [2] = 2, [1] = 1 });
        x.ToHashCode().ShouldBe(y.ToHashCode());
    }
}
