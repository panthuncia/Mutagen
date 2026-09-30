using System.Reflection;
using Loqui;
using Loqui.Internal;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

/// <summary>
/// <c>FillDifferingFields</c> marks exactly the fields in which two records differ, each field as the generated Equals
/// restricted to that field says.
/// </summary>
public class DifferingFieldsTests
{
    private static bool[] Differing(IMajorRecordGetter lhs, IMajorRecordGetter rhs)
    {
        var differs = new bool[((ILoquiObject)lhs).Registration.FieldCount];
        lhs.FillDifferingFields(rhs, differs);
        return differs;
    }

    [Fact]
    public void MarksTheFieldsThatDiffer()
    {
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var npc = mod.Npcs.AddNew("Npc");
        npc.Name = "Before";
        var other = (Npc)npc.DeepCopy();
        Differing(npc, other).ShouldAllBe(d => !d);

        other.Name = "After";
        other.Race.SetTo(FormKey.Factory("123456:Skyrim.esm"));
        var differs = Differing(npc, other);
        Enumerable.Range(0, differs.Length).Where(i => differs[i]).Select(i => (Npc_FieldIndex)i)
            .ShouldBe([Npc_FieldIndex.Race, Npc_FieldIndex.Name], ignoreOrder: true);
    }

    [Fact]
    public void AgreesWithEqualsFieldByField()
    {
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var records = new List<(MajorRecord, MajorRecord)>();
        var npc = mod.Npcs.AddNew("Npc");
        npc.Name = "Name";
        npc.Factions.Add(new RankPlacement { Rank = 1 });
        var npc2 = (Npc)npc.DeepCopy();
        npc2.Factions[0].Rank = 2;
        npc2.EditorID = "Other";
        records.Add((npc, npc2));
        var weapon = mod.Weapons.AddNew("Weapon");
        weapon.Data = new WeaponData { Speed = 1 };
        var weapon2 = (Weapon)weapon.DeepCopy();
        weapon2.Data!.Speed = 2;
        weapon2.Keywords = [FormKey.Factory("000123:Skyrim.esm").ToLink<IKeywordGetter>()];
        records.Add((weapon, weapon2));
        var list = mod.LeveledItems.AddNew("List");
        list.Entries = [new LeveledItemEntry { Data = new LeveledItemEntryData { Count = 1, Level = 1 } }];
        var list2 = (LeveledItem)list.DeepCopy();
        list2.Entries!.Add(new LeveledItemEntry { Data = new LeveledItemEntryData { Count = 2, Level = 3 } });
        records.Add((list, list2));
        var landscape = new Landscape(mod) { VertexNormals = new Noggog.Array2d<Noggog.P3UInt8>(3, 3, new Noggog.P3UInt8(1, 2, 3)) };
        var landscape2 = (Landscape)landscape.DeepCopy();
        landscape2.VertexNormals![1, 1] = new Noggog.P3UInt8(4, 5, 6);
        records.Add((landscape, landscape2));

        foreach (var (lhs, rhs) in records)
        {
            var differs = Differing(lhs, rhs);
            differs.ShouldContain(true);
            for (var field = 0; field < differs.Length; field++)
            {
                differs[field].ShouldBe(!EqualsOnly(lhs, rhs, field), $"{lhs.GetType().Name} field {field}");
            }
        }
    }

    [Fact]
    public void OverlaysMarkWhatTheirRecordsDo()
    {
        using var folder = TempFolder.Factory();
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var npc = mod.Npcs.AddNew("Npc");
        npc.Name = "Name";
        var other = mod.Npcs.AddNew("Other");
        other.Name = "Other name";
        var path = Path.Combine(folder.Dir, mod.ModKey.FileName);
        mod.WriteToBinary(path);
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        var read = overlay.Npcs.ToDictionary(n => n.EditorID!);
        Differing(read["Npc"], read["Other"]).ShouldBe(Differing(npc, other));
    }

    /// <summary>The generated Equals, restricted to one field.</summary>
    private static bool EqualsOnly(IMajorRecordGetter lhs, IMajorRecordGetter rhs, int field)
    {
        var registration = ((ILoquiObject)lhs).Registration;
        var maskType = registration.ClassType.GetNestedType("TranslationMask")!;
        var mask = System.Activator.CreateInstance(maskType, false, true)!;
        var fieldIndex = registration.ClassType.Assembly.GetType(registration.ClassType.FullName + "_FieldIndex")!;
        var name = Enum.GetName(fieldIndex, field)!;
        var member = maskType.GetField(name, BindingFlags.Public | BindingFlags.Instance)!;
        member.SetValue(mask, member.FieldType == typeof(bool) ? true : System.Activator.CreateInstance(member.FieldType, true, true));
        var crystal = (TranslationCrystal)maskType.GetMethod("GetCrystal", Type.EmptyTypes)!.Invoke(mask, null)!;
        var common = registration.ClassType.Assembly.GetType(registration.ClassType.FullName + "Common")
                     ?? typeof(MajorRecord).Assembly.GetType(registration.ClassType.FullName + "Common")!;
        var instance = common.GetField("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        var equals = common.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Equals" && m.GetParameters() is [var p, _, var c] && p.ParameterType == registration.GetterType && c.ParameterType == typeof(TranslationCrystal));
        return (bool)equals.Invoke(instance, [lhs, rhs, crystal])!;
    }
}