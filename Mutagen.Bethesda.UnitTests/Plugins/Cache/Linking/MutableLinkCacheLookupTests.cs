using Loqui;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Mapping;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Cache.Linking;

/// <summary>
/// A mutable mod's link cache finds records of top-level types through the mod's groups rather than by enumerating
/// it. It must find exactly what enumerating finds, for every type it can be asked for, and see the mod as it is now.
/// </summary>
public class MutableLinkCacheLookupTests
{
    private static SkyrimMod VariedMod()
    {
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        mod.Npcs.AddNew("Npc");
        mod.LeveledNpcs.AddNew("LeveledNpc");
        mod.Armors.AddNew("Armor");
        mod.Weapons.AddNew("Weapon");
        mod.Keywords.AddNew("Keyword");
        mod.Globals.Add(new GlobalInt(mod, "GlobalInt"));
        mod.Globals.Add(new GlobalFloat(mod, "GlobalFloat"));
        mod.GameSettings.Add(new GameSettingFloat(mod, "fSetting"));
        var topic = mod.DialogTopics.AddNew("Topic");
        topic.Responses.Add(new DialogResponses(mod));
        var cell = new Cell(mod, "Interior") { Flags = Cell.Flag.IsInteriorCell };
        cell.Temporary.Add(new PlacedObject(mod, "Reference"));
        mod.Cells.Records.Add(new CellBlock
        {
            BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks = [new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = [cell] }],
        });
        return mod;
    }

    /// <summary>Every record type in the mod (getter and setter), every Skyrim link interface, and the general getters.</summary>
    private static IEnumerable<Type> QueryTypes(SkyrimMod mod)
    {
        var registrations = mod.EnumerateMajorRecords().Select(r => ((ILoquiObject)r).Registration).Distinct();
        return registrations.SelectMany(r => new[] { r.GetterType, r.SetterType })
            .Concat(LinkInterfaceMapping.Instance.InterfaceToObjectTypes(GameCategory.Skyrim).Keys)
            .Concat([typeof(IGlobalGetter), typeof(IGlobalIntGetter), typeof(IGlobalFloatGetter), typeof(IGameSettingGetter),
                typeof(IGameSettingIntGetter), typeof(IMajorRecordGetter), typeof(ISkyrimMajorRecordGetter)])
            .Distinct();
    }

    private static IMajorRecordGetter? Enumerated(IModGetter mod, FormKey formKey, Type type)
    {
        try
        {
            // Enumerating by a record subtype's interface (IGlobalIntGetter) yields every record of the base type (every
            // global); only records of the type asked for are a match.
            return mod.EnumerateMajorRecords(type).FirstOrDefault(r => r.FormKey == formKey && type.IsInstanceOfType(r));
        }
        catch (Exception)
        {
            // The cache's enumeration swallows what enumerating throws for types a mod does not know.
            return null;
        }
    }

    [Fact]
    public void FindsWhatEnumeratingFinds()
    {
        var mod = VariedMod();
        var cache = mod.ToMutableLinkCache();
        var formKeys = mod.EnumerateMajorRecords().Select(r => r.FormKey).Append(new FormKey(mod.ModKey, 0xABCDEF)).ToArray();
        var checkedPairs = 0;
        foreach (var type in QueryTypes(mod))
        {
            foreach (var formKey in formKeys)
            {
                var expected = Enumerated(mod, formKey, type);
                cache.TryResolve(formKey, type, out var actual).ShouldBe(expected != null, $"{type.Name} {formKey}");
                if (expected != null) actual.ShouldBeSameAs(expected, $"{type.Name} {formKey}");
                checkedPairs++;
            }
        }
        checkedPairs.ShouldBeGreaterThan(100);
    }

    [Fact]
    public void ResolvesOnlyRecordsOfTheSubtypeAskedFor()
    {
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var floatGlobal = new GlobalFloat(mod, "GlobalFloat");
        mod.Globals.Add(floatGlobal);
        var cache = mod.ToMutableLinkCache();
        cache.TryResolve<IGlobalIntGetter>(floatGlobal.FormKey, out _).ShouldBeFalse();
        cache.TryResolve(floatGlobal.FormKey, typeof(IGlobalIntGetter), out _).ShouldBeFalse();
        cache.TryResolve<IGlobalFloatGetter>(floatGlobal.FormKey, out var asFloat).ShouldBeTrue();
        asFloat.ShouldBeSameAs(floatGlobal);
        cache.TryResolve<IGlobalGetter>(floatGlobal.FormKey, out var asGlobal).ShouldBeTrue();
        asGlobal.ShouldBeSameAs(floatGlobal);
    }

    [Fact]
    public void SeesTheModAsItIsNow()
    {
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var cache = mod.ToMutableLinkCache();
        var formKey = mod.GetNextFormKey();
        cache.TryResolve<INpcGetter>(formKey, out _).ShouldBeFalse();

        var npc = new Npc(formKey, SkyrimRelease.SkyrimSE);
        mod.Npcs.Add(npc);
        cache.TryResolve<INpcGetter>(formKey, out var found).ShouldBeTrue();
        found.ShouldBeSameAs(npc);
        cache.TryResolve<INpcSpawnGetter>(formKey, out var spawn).ShouldBeTrue();
        spawn.ShouldBeSameAs(npc);

        mod.Npcs.Remove(formKey);
        cache.TryResolve<INpcGetter>(formKey, out _).ShouldBeFalse();
        cache.TryResolve<INpcSpawnGetter>(formKey, out _).ShouldBeFalse();
    }

    [Fact]
    public void ContextsResolveForRecordsTheModHolds()
    {
        var mod = VariedMod();
        var cache = mod.ToMutableLinkCache();
        var npc = mod.Npcs.First();
        cache.TryResolveContext<INpc, INpcGetter>(npc.FormKey, out var context).ShouldBeTrue();
        context.Record.ShouldBeSameAs(npc);
        cache.TryResolveSimpleContext<INpcGetter>(npc.FormKey, out var simple).ShouldBeTrue();
        simple.Record.ShouldBeSameAs(npc);
        var reference = mod.Cells.Records.Single().SubBlocks.Single().Cells.Single().Temporary.Single();
        cache.TryResolveContext<IPlacedObject, IPlacedObjectGetter>(reference.FormKey, out var placed).ShouldBeTrue();
        placed.Record.ShouldBeSameAs(reference);

        var missing = new FormKey(mod.ModKey, 0xABCDEF);
        cache.TryResolveContext<INpc, INpcGetter>(missing, out _).ShouldBeFalse();
        cache.TryResolveSimpleContext(missing, typeof(INpcGetter), out _).ShouldBeFalse();
    }
}