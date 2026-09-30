using Loqui;
using Loqui.Generation;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;

namespace Mutagen.Bethesda.Generation.Modules.Plugin;

/// <summary>
/// Generates <c>FillDifferingFields</c> for major records: which of two records' fields differ, each field compared as
/// the generated <c>Equals</c> compares it, marked by field index in a span the caller provides. Nothing is allocated
/// and every field is compared, where <c>Equals</c> stops at the first difference and an equals mask builds an object
/// for every field and element. It is what conflict detection needs to know of each pair of versions.
/// <para/>
/// Each field's comparison is its <c>GenerateForEquals</c> output, wrapped in a local function that returns true after
/// it, so the <c>return false</c> it emits means "this field differs". Base class fields are filled by the base class's
/// method, as <c>Equals</c> calls its base class's.
/// </summary>
public class DifferingFieldsModule : GenerationModule
{
    private const string Method = "FillDifferingFields";

    public override async Task GenerateInCommon(ObjectGeneration obj, StructuredStringBuilder sb, MaskTypeSet maskTypes)
    {
        if (!maskTypes.Applicable(LoquiInterfaceType.IGetter, CommonGenerics.Class)) return;
        if (!await obj.IsMajorRecord() || obj.Generics.Count > 0) return;

        using (var args = sb.Function($"public virtual void {Method}"))
        {
            args.Add($"{obj.Interface(getter: true, internalInterface: true)} lhs");
            args.Add($"{obj.Interface(getter: true, internalInterface: true)} rhs");
            args.Add("Span<bool> differs");
            args.Add("TranslationCrystal? equalsMask");
        }
        using (sb.CurlyBrace())
        {
            if (obj.HasLoquiBaseObject)
            {
                var baseInterface = obj.BaseClass!.Interface(getter: true, internalInterface: true);
                sb.AppendLine($"base.{Method}(({baseInterface})lhs, ({baseInterface})rhs, differs, equalsMask);");
            }
            var index = 0;
            foreach (var field in obj.IterateFields())
            {
                if (obj.HasKeyField() && !field.KeyField) continue;
                var fieldSb = new StructuredStringBuilder();
                field.GenerateForEquals(fieldSb, Accessor.FromType(field, "lhs"), Accessor.FromType(field, "rhs"), "equalsMask");
                if (fieldSb.Count == 0) continue;
                var local = $"Equal{index++}";
                sb.AppendLine($"if (!{local}()) differs[{field.IndexEnumInt}] = true;");
                sb.AppendLine($"bool {local}()");
                using (sb.CurlyBrace())
                {
                    sb.AppendLines(fieldSb);
                    sb.AppendLine("return true;");
                }
            }
        }
        sb.AppendLine();

        foreach (var baseClass in obj.BaseClassTrail())
        {
            using (var args = sb.Function($"public override void {Method}"))
            {
                args.Add($"{baseClass.Interface(getter: true, internalInterface: true)} lhs");
                args.Add($"{baseClass.Interface(getter: true, internalInterface: true)} rhs");
                args.Add("Span<bool> differs");
                args.Add("TranslationCrystal? equalsMask");
            }
            using (sb.CurlyBrace())
            {
                var own = obj.Interface(getter: true, internalInterface: true);
                sb.AppendLine($"{Method}(({own})lhs, ({own})rhs, differs, equalsMask);");
            }
            sb.AppendLine();
        }
    }

    public override async Task GenerateInCommonMixin(ObjectGeneration obj, StructuredStringBuilder sb)
    {
        if (!await obj.IsMajorRecord() || obj.Generics.Count > 0) return;
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Marks, by field index, the fields in which the two records differ, each compared as Equals compares it.");
        sb.AppendLine("/// Marks are only ever set: <paramref name=\"differs\"/> needs a flag for every field of the records' type (its");
        sb.AppendLine("/// registration's FieldCount), cleared. Both records must be of the same type.");
        sb.AppendLine("/// </summary>");
        using (var args = sb.Function($"public static void {Method}"))
        {
            args.Add($"this {obj.Interface(getter: true, internalInterface: false)} item");
            args.Add($"{obj.Interface(getter: true, internalInterface: false)} rhs");
            args.Add("Span<bool> differs");
            args.Add("TranslationCrystal? equalsMask = null");
        }
        using (sb.CurlyBrace())
        {
            using (var args = sb.Call($"{obj.CommonClassInstance("item", LoquiInterfaceType.IGetter, CommonGenerics.Class)}.{Method}"))
            {
                args.Add("lhs: item");
                args.Add("rhs: rhs");
                args.Add("differs: differs");
                args.Add("equalsMask: equalsMask");
            }
        }
        sb.AppendLine();
    }
}