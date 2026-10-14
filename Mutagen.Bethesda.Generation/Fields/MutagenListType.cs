using Loqui.Generation;
using System.Xml.Linq;
using Mutagen.Bethesda.Generation.Modules.Binary;
using Noggog;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;

namespace Mutagen.Bethesda.Generation.Fields;

public class MutagenListType : ListType
{
    private bool _scalarOverlayEquals;

    public override async Task Load(XElement node, bool requireName = true)
    {
        await base.Load(node, requireName);
        _scalarOverlayEquals = node.GetAttribute("scalarOverlayEquals", false);
    }

    public override void GenerateForEquals(StructuredStringBuilder sb, Accessor accessor, Accessor rhsAccessor, Accessor maskAccessor)
    {
        if (_scalarOverlayEquals)
        {
            GenerateScalarEquals(sb, accessor, rhsAccessor, maskAccessor);
            return;
        }
        // Byte arrays are memory slices, whose own Equals compares where they point rather than their bytes.
        // Compare the elements the way the equals mask does.
        if (SubTypeGeneration is ByteArrayType byteArray)
        {
            sb.AppendLine($"if ({this.GetTranslationIfAccessor(maskAccessor)})");
            using (sb.CurlyBrace())
            {
                sb.AppendLine($"if (!{accessor.Access}.SequenceEqualNullable({rhsAccessor.Access}, (l, r) => {byteArray.GenerateEqualsSnippet(new Accessor("l"), new Accessor("r"))})) return false;");
            }
            return;
        }
        base.GenerateForEquals(sb, accessor, rhsAccessor, maskAccessor);
    }

    private void GenerateScalarEquals(StructuredStringBuilder sb, Accessor accessor, Accessor rhsAccessor, Accessor maskAccessor)
    {
        if (SubTypeGeneration is not LoquiType { TargetObjectGeneration: { } target }
            || target.BaseClass != null)
            throw new ArgumentException("Scalar overlay equality requires a direct, non-inherited fixed-layout object.");
        var comparisons = new List<string>();
        var offset = 0;
        foreach (var field in target.Fields)
        {
            var data = field.GetFieldData();
            if (field.Nullable || data.HasTrigger || data.HasVersioning || data.HasModHeaderVersioning
                || data.Binary != BinaryGenerationType.Normal || data.BinaryOverlayFallback != BinaryGenerationType.Normal)
                throw new ArgumentException($"Unsupported scalar overlay field: {field.Name}");
            int length;
            string Read(string side)
            {
                if (field is UInt16Type)
                    return $"System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian({side}.Slice({offset}, 2))";
                return $"FloatBinaryTranslation<MutagenFrame, MutagenWriter>.Instance.GetFloat({side}.Slice({offset}, 4))";
            }
            if (field is UInt16Type && (data.Length == null || data.Length == 2)) length = 2;
            else if (field is FloatType { HasTransformations: false, IntegerType: null }
                     && (data.Length == null || data.Length == 4)) length = 4;
            else throw new ArgumentException($"Unsupported scalar overlay field: {field.Name}");
            comparisons.Add($"(!(mask?.GetShouldTranslate({field.IndexEnumInt}) ?? true) || {field.GenerateEqualsSnippet(new Accessor(Read("l")), new Accessor(Read("r")))})");
            offset += length;
        }
        if (offset == 0) throw new ArgumentException("Scalar overlay equality requires at least one field.");
        sb.AppendLine($"if ({this.GetTranslationIfAccessor(maskAccessor)} && ScalarOverlayEquality.TryEquals({accessor.Access}, {rhsAccessor.Access}, {offset},");
        sb.AppendLine($"    static (l, r, mask) => {string.Join(" && ", comparisons)}, {maskAccessor}?.GetSubCrystal({IndexEnumInt}), out var scalar{ Name }Equal))");
        using (sb.CurlyBrace()) sb.AppendLine($"if (!scalar{ Name }Equal) return false;");
        sb.AppendLine("else");
        using (sb.CurlyBrace()) base.GenerateForEquals(sb, accessor, rhsAccessor, maskAccessor);
    }

    public override void GenerateForHash(StructuredStringBuilder sb, Accessor accessor, string hashResultAccessor) =>
        ContentHashGeneration.Generate(sb, accessor, hashResultAccessor);

    public override void GenerateForEqualsMask(StructuredStringBuilder sb, Accessor accessor, Accessor rhsAccessor, string retAccessor) =>
        EqualsMaskGeneration.Generate(sb, this, "ListEqualsMask");
}
