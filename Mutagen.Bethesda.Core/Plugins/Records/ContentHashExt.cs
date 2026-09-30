using Noggog;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// Adds a field's contents to a hash, rather than the field object: generated <c>GetHashCode</c> uses these for
/// lists, dictionaries, 2D arrays and byte arrays, whose own hash codes are those of the object, so that records
/// equal by <c>Equals</c> (which compares contents) hash alike.
/// </summary>
public static class ContentHashExt
{
    public static void AddContents<T>(ref this HashCode hash, IEnumerable<T>? items)
    {
        if (items is null) return;
        foreach (var item in items)
        {
            hash.Add(item);
        }
    }

    public static void AddContents(ref this HashCode hash, IEnumerable<ReadOnlyMemorySlice<byte>>? items)
    {
        if (items is null) return;
        foreach (var item in items)
        {
            hash.AddBytes(item.Span);
        }
    }

    /// <summary>Order-independent, so dictionaries equal in any order hash alike.</summary>
    public static void AddContents<TKey, TValue>(ref this HashCode hash, IEnumerable<KeyValuePair<TKey, TValue>>? items)
    {
        if (items is null) return;
        var sum = 0;
        foreach (var item in items)
        {
            sum += HashCode.Combine(item.Key, item.Value);
        }
        hash.Add(sum);
    }

    public static void AddContents<T>(ref this HashCode hash, IReadOnlyArray2d<T>? items)
    {
        if (items is null) return;
        for (var y = 0; y < items.Height; y++)
        {
            for (var x = 0; x < items.Width; x++)
            {
                hash.Add(items[x, y]);
            }
        }
    }

    public static void AddContents<T>(ref this HashCode hash, ReadOnlyMemorySlice<T> items)
    {
        foreach (var item in items.Span)
        {
            hash.Add(item);
        }
    }

    public static void AddContents<T>(ref this HashCode hash, ReadOnlyMemorySlice<T>? items)
    {
        if (items is { } i)
        {
            hash.AddContents(i);
        }
    }

    public static void AddContents(ref this HashCode hash, ReadOnlyMemorySlice<byte> bytes)
    {
        hash.AddBytes(bytes.Span);
    }

    public static void AddContents(ref this HashCode hash, ReadOnlyMemorySlice<byte>? bytes)
    {
        if (bytes is { } b)
        {
            hash.AddBytes(b.Span);
        }
    }
}
