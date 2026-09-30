using Loqui.Generation;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;

namespace Mutagen.Bethesda.Generation.Fields;

public class MutagenArray2dType : Array2dType
{
    public override void GenerateForHash(StructuredStringBuilder sb, Accessor accessor, string hashResultAccessor) =>
        ContentHashGeneration.Generate(sb, accessor, hashResultAccessor);

    // Compared through the indexer (Array2dEquality), not as a sequence of boxed key-value pairs.
    public override string GenerateEqualsSnippet(Accessor accessor, Accessor rhsAccessor, bool negate = false) =>
        $"{(negate ? "!" : null)}{accessor.Access}.Array2dEquals({rhsAccessor.Access})";

    public override void GenerateForEquals(StructuredStringBuilder sb, Accessor accessor, Accessor rhsAccessor, Accessor maskAccessor)
    {
        sb.AppendLine($"if ({GetTranslationIfAccessor(maskAccessor)})");
        using (sb.CurlyBrace())
        {
            if (SubTypeGeneration is LoquiType { TargetObjectGeneration: not null } loqui)
            {
                var common = loqui.TargetObjectGeneration.CommonClassSpeccedInstance("l", LoquiInterfaceType.IGetter, CommonGenerics.Class, loqui.GenericSpecification);
                sb.AppendLine($"if (!{accessor.Access}.Array2dEquals({rhsAccessor.Access}, (l, r) => {common}.Equals(l, r, {maskAccessor}?.GetSubCrystal({IndexEnumInt})))) return false;");
            }
            else
            {
                sb.AppendLine($"if (!{accessor.Access}.Array2dEquals({rhsAccessor.Access})) return false;");
            }
        }
    }

    public override void GenerateForEqualsMask(StructuredStringBuilder sb, Accessor accessor, Accessor rhsAccessor, string retAccessor) =>
        EqualsMaskGeneration.Generate(sb, this, "Array2dEqualsMask");
}
