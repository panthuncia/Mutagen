using Mutagen.Bethesda.Plugins.Binary.Overlay;
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
    public static bool Array2dEquals<T>(this IReadOnlyArray2d<T>? lhs, IReadOnlyArray2d<T>? rhs)
    {
        if (lhs is IBinaryOverlayArray2d left && rhs is IBinaryOverlayArray2d right
            && left.ItemLength == right.ItemLength && lhs.Width == rhs.Width && lhs.Height == rhs.Height)
        {
            return BytesEqual(lhs, rhs, left.Bytes, right.Bytes, left.ItemLength);
        }
        return lhs.Array2dEquals(rhs, EqualityComparer<T>.Default.Equals);
    }

    /// <summary>
    /// Two arrays read from plugins: elements whose bytes are the same are equal, so only elements whose bytes differ
    /// are decoded and compared (different bytes can still read as equal values, as a bool stored as 1 or as 2 does).
    /// </summary>
    private static bool BytesEqual<T>(IReadOnlyArray2d<T> lhs, IReadOnlyArray2d<T> rhs, ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, int itemLength)
    {
        if (left.Length != right.Length || left.Length != lhs.Width * lhs.Height * itemLength)
        {
            return lhs.Array2dEquals(rhs, EqualityComparer<T>.Default.Equals);
        }
        var comparer = EqualityComparer<T>.Default;
        var offset = 0;
        while (offset < left.Length)
        {
            var same = left[offset..].CommonPrefixLength(right[offset..]);
            if (same == left.Length - offset) return true;
            var index = (offset + same) / itemLength;
            var (x, y) = (index % lhs.Width, index / lhs.Width);
            if (!comparer.Equals(lhs[x, y], rhs[x, y])) return false;
            offset = (index + 1) * itemLength;
        }
        return true;
    }

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
