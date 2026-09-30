using Loqui.Generation;
using Noggog.StructuredStrings;

namespace Mutagen.Bethesda.Generation.Fields;

public class MutagenArrayType : ArrayType
{
    public override void GenerateForHash(StructuredStringBuilder sb, Accessor accessor, string hashResultAccessor) =>
        ContentHashGeneration.Generate(sb, accessor, hashResultAccessor);
}
