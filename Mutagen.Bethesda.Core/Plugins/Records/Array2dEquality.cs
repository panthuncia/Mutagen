using Noggog;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// Whether two 2D arrays hold the same values, element by element through the indexer. Generated <c>Equals</c> used to
/// compare them as sequences of key-value pairs, which boxes every element and compares the pairs with the default
/// struct equality (by reflection); a landscape's 33x33 arrays made that most of the time spent comparing records.
/// </summary>
public static class Array2dEquality
{
    /// <summary>Equal when both are missing, or both have the same size and equal elements.</summary>
    public static bool Array2dEquals<T>(this IReadOnlyArray2d<T>? lhs, IReadOnlyArray2d<T>? rhs) =>
        lhs.Array2dEquals(rhs, EqualityComparer<T>.Default.Equals);

    /// <summary>Equal when both are missing, or both have the same size and each pair of elements is equal.</summary>
    public static bool Array2dEquals<T>(this IReadOnlyArray2d<T>? lhs, IReadOnlyArray2d<T>? rhs, Func<T, T, bool> equals)
    {
        if (ReferenceEquals(lhs, rhs)) return true;
        if (lhs == null || rhs == null) return false;
        if (lhs.Width != rhs.Width || lhs.Height != rhs.Height) return false;
        for (var y = 0; y < lhs.Height; y++)
        {
            for (var x = 0; x < lhs.Width; x++)
            {
                if (!equals(lhs[x, y], rhs[x, y])) return false;
            }
        }
        return true;
    }
}
