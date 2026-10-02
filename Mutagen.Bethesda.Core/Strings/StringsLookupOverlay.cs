using Mutagen.Bethesda.Plugins.Binary.Translations;
using Noggog;
using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;

namespace Mutagen.Bethesda.Strings;

/// <summary>
/// Class that does minimal processing on string file data, exposing lookup queries in a lazy on-demand fashion.
/// </summary>
public sealed class StringsLookupOverlay : IStringsLookup
{
    // The strings' keys, sorted, and their locations: 8 bytes a string, where a dictionary took about 20. A game's
    // localized plugins hold hundreds of thousands of strings, for as long as their plugins are open.
    private uint[] _keys = [];
    private int[] _locations = [];
    private ReadOnlyMemorySlice<byte> _directory;
    private ReadOnlyMemorySlice<byte> _stringData;
    private IMutagenEncoding _encoding = null!;
        
    public StringsFileFormat Type { get; private set; }
    public int Count => _keys.Length;
    public string? AssociatedPath { get; }

    /// <summary>
    /// Overlays onto a set of bytes assumed to be in Strings file format
    /// </summary>
    /// <param name="data">Data to wrap</param>
    /// <param name="type">Strings file format</param>
    /// <param name="encoding">Encoding to read strings with</param>
    public StringsLookupOverlay(ReadOnlyMemorySlice<byte> data, StringsFileFormat type, IMutagenEncoding encoding)
    {
        Init(data, type, encoding);
    }

    /// <summary>
    /// Overlays onto a set of bytes assumed to be in Strings file format
    /// </summary>
    /// <param name="data">Data to wrap</param>
    /// <param name="source">Source type</param>
    /// <param name="encoding">Encoding to read strings with</param>
    public StringsLookupOverlay(ReadOnlyMemorySlice<byte> data, StringsSource source, IMutagenEncoding encoding)
    {
        Init(data, StringsUtility.GetFormat(source), encoding);
    }

    /// <summary>
    /// Reads all bytes from a file, and overlays them
    /// </summary>
    /// <param name="path">Path to read in</param>
    /// <param name="type">Strings file format</param>
    /// <param name="encoding">Encoding to read strings with</param>
    public StringsLookupOverlay(string path, StringsFileFormat type, IMutagenEncoding encoding)
    {
        AssociatedPath = path;
        Init(File.ReadAllBytes(path), type, encoding);
    }

    /// <summary>
    /// Reads all bytes from a file, and overlays them
    /// </summary>
    /// <param name="path">Path to read in</param>
    /// <param name="source">Source type</param>
    /// <param name="encoding">Encoding to read strings with</param>
    /// <param name="fileSystem">Filesystem to use</param>
    public StringsLookupOverlay(string path, StringsSource source, IMutagenEncoding encoding, IFileSystem? fileSystem = null)
    {
        AssociatedPath = path;
        fileSystem ??= fileSystem.GetOrDefault();
        Init(fileSystem.File.ReadAllBytes(path), StringsUtility.GetFormat(source), encoding);
    }

    private void Init(ReadOnlyMemorySlice<byte> data, StringsFileFormat type, IMutagenEncoding encoding)
    {
        try
        {
            _encoding = encoding;
            Type = type;
            var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
            var dataSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4)));
            var indexData = data.Slice(8, checked((int)(count * 2 * 4)));
            var keys = new uint[count];
            var locations = new int[count];
            int loc = 0;
            for (int i = 0; i < count; i++)
            {
                keys[i] = BinaryPrimitives.ReadUInt32LittleEndian(indexData.Slice(loc));
                locations[i] = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(indexData.Slice(loc + 4)));
                loc += 8;
            }
            Array.Sort(keys, locations);
            for (int i = 1; i < keys.Length; i++)
            {
                if (keys[i] == keys[i - 1]) throw new ArgumentException("Strings file had duplicate entries.");
            }
            _keys = keys;
            _locations = locations;
            _directory = indexData;
            _stringData = data.Slice(8 + indexData.Length, dataSize);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("Strings file had duplicate entries.");
        }
        catch (OverflowException)
        {
            throw new ArgumentException("Strings file was too big for current systems");
        }
    }

    /// <inheritdoc />
    public bool TryLookup(uint key, [MaybeNullWhen(false)] out string str)
    {
        if (!TryGetLocation(key, out var loc))
        {
            str = default;
            return false;
        }

        str = GetStringAtLocation(loc);
        return true;
    }

    public bool TryGetLocation(uint stringsKey, out int loc)
    {
        var i = Array.BinarySearch(_keys, stringsKey);
        loc = i >= 0 ? _locations[i] : default;
        return i >= 0;
    }

    public string GetStringAtLocation(int loc)
    {
        return _encoding.GetString(GetStringBytesAtLocation(loc));
    }

    public ReadOnlySpan<byte> GetStringBytesAtLocation(int loc)
    {
        switch (Type)
        {
            case StringsFileFormat.Normal:
                return BinaryStringUtility.ExtractUnknownLengthString(_stringData.Slice(loc));
            case StringsFileFormat.LengthPrepended:
                try
                {
                    var extract = BinaryStringUtility.ExtractPrependedString(_stringData.Slice(loc), 4);
                    extract = BinaryStringUtility.ProcessNullTermination(extract);
                    return extract;
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new ArgumentOutOfRangeException("Strings file malformed.");
                }
            default:
                throw new NotImplementedException();
        }
    }

    /// <summary>The strings in the file's order.</summary>
    public IEnumerator<KeyValuePair<uint, string>> GetEnumerator()
    {
        for (int at = 0; at < _directory.Length; at += 8)
        {
            var key = BinaryPrimitives.ReadUInt32LittleEndian(_directory.Slice(at));
            var loc = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(_directory.Slice(at + 4)));
            yield return new KeyValuePair<uint, string>(key, GetStringAtLocation(loc));
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}