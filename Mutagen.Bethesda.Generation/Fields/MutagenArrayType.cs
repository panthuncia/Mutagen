using Loqui.Generation;
using Noggog.StructuredStrings;

namespace Mutagen.Bethesda.Generation.Fields;

public class MutagenArrayType : ArrayType
{
    public override void GenerateForHash(StructuredStringBuilder sb, Accessor accessor, string hashResultAccessor) =>
        ContentHashGeneration.Generate(sb, accessor, hashResultAccessor);

    public override void GenerateForEqualsMask(StructuredStringBuilder sb, Accessor accessor, Accessor rhsAccessor, string retAccessor)
    {
        // An array of simple values is a memory slice in the getter, which Loqui compares as a span.
        if (SimpleTarget)
        {
            base.GenerateForEqualsMask(sb, accessor, rhsAccessor, retAccessor);
            return;
        }
        EqualsMaskGeneration.Generate(sb, this, "ListEqualsMask");
    }
}
