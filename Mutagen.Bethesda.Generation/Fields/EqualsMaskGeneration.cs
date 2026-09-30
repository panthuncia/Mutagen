using Loqui.Generation;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;

namespace Mutagen.Bethesda.Generation.Fields;

/// <summary>
/// The equals mask of a list or 2D array field, through Mutagen's <c>EqualsMasks</c> rather than Loqui's
/// <c>EqualsMaskHelper</c>: the same mask, built without growing lists or capturing <c>include</c> in the comparer.
/// </summary>
public static class EqualsMaskGeneration
{
    public static void Generate(StructuredStringBuilder sb, ContainerType container, string method)
    {
        var comparer = container.SubTypeGeneration is LoquiType loqui
            ? $"(loqLhs, loqRhs, incl) => {(loqui.TargetObjectGeneration == null ? "(IMask<bool>)" : null)}loqLhs.GetEqualsMask(loqRhs, incl)"
            : $"(l, r) => {container.SubTypeGeneration.GenerateEqualsSnippet(new Accessor("l"), new Accessor("r"))}";
        using (var args = sb.Call($"ret.{container.Name} = item.{container.Name}.{method}"))
        {
            args.Add($"rhs.{container.Name}");
            args.Add(comparer);
            args.Add("include");
        }
    }
}
