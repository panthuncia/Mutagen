using Loqui;
using Loqui.Internal;
using Mutagen.Bethesda.Plugins.Binary.Overlay;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>Generated scalar comparisons over fixed-size overlay elements, without materializing the elements.</summary>
public static class ScalarOverlayEquality
{
    public delegate bool ElementEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, TranslationCrystal? mask);

    public static bool TryEquals<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right, int itemLength,
        ElementEquals equals, TranslationCrystal? mask, out bool equal)
    {
        equal = false;
        if (left is not BinaryOverlayList.BinaryOverlayListByStartIndex<T> lhs
            || right is not BinaryOverlayList.BinaryOverlayListByStartIndex<T> rhs
            || itemLength <= 0 || lhs.ItemLength != itemLength || rhs.ItemLength != itemLength
            || lhs.Memory.Length % itemLength != 0 || rhs.Memory.Length % itemLength != 0)
            return false;
        if (lhs.Count != rhs.Count) return true;
        var l = lhs.Memory.Span;
        var r = rhs.Memory.Span;
        for (var at = 0; at < l.Length; at += itemLength)
        {
            if (!equals(l.Slice(at, itemLength), r.Slice(at, itemLength), mask)) return true;
        }
        equal = true;
        return true;
    }
}
