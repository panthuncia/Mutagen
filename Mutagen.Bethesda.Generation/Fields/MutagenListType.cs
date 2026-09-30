using Loqui.Generation;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;

namespace Mutagen.Bethesda.Generation.Fields;

public class MutagenListType : ListType
{
    public override void GenerateForEquals(StructuredStringBuilder sb, Accessor accessor, Accessor rhsAccessor, Accessor maskAccessor)
    {
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
}
