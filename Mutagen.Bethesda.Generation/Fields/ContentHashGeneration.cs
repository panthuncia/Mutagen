using Loqui.Generation;
using Noggog.StructuredStrings;

namespace Mutagen.Bethesda.Generation.Fields;

/// <summary>
/// Hashes a container or byte array by its contents (<c>ContentHashExt.AddContents</c>), as its generated
/// <c>Equals</c> compares it. Loqui's default adds the field object, whose hash is the object's.
/// </summary>
public static class ContentHashGeneration
{
    public static void Generate(StructuredStringBuilder sb, Accessor accessor, string hashResultAccessor)
    {
        sb.AppendLine($"{hashResultAccessor}.AddContents({accessor.Access});");
    }
}
