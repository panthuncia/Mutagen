using System.Buffers.Binary;
using System.IO.Abstractions;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Internals;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Analysis;

/// <summary>
/// Every major record in one plugin, in file order, read from the plugin's bytes without parsing records: each record's
/// FormID as stored, record type, flags, position in the file, parent (the record whose children group holds it: a
/// cell's worldspace, a placed object's cell, a dialog response's topic) and EditorID. Only record headers are read,
/// plus each record's first subrecord when it is an EditorID, decompressing only that far when the record is
/// compressed. <see cref="LoadOrderRecordIndex"/> turns the plugins of a load order into FormKeys and their versions.
/// </summary>
public sealed class PluginRecordIndex
{
    /// <summary>Plugins at least this large are scanned in parallel pieces (top-level groups, exterior cell blocks).</summary>
    internal const int SplitThreshold = 16 << 20;

    private const int FormatVersion = 1;
    private static readonly byte[] FormatMagic = "MRIX"u8.ToArray();

    private uint[] _formIds;
    private int[] _recordTypes;
    private int[] _flags;
    private long[] _positions;
    private int[] _parents;
    private int[] _editorIdStarts;
    private ushort[] _editorIdLengths;
    private byte[] _editorIdPool;

    /// <summary>The plugin.</summary>
    public ModKey ModKey { get; }

    /// <summary>The game release the plugin was read as.</summary>
    public GameRelease Release { get; }

    /// <summary>The plugin's own master style, from its header flags.</summary>
    public MasterStyle MasterStyle { get; }

    /// <summary>The plugin's masters, as its header lists them.</summary>
    public IReadOnlyList<ModKey> Masters { get; }

    /// <summary>How many records the plugin holds.</summary>
    public int Count { get; private set; }

    /// <summary>Each record's FormID as stored in the plugin; <see cref="LoadOrderRecordIndex"/> turns them into FormKeys.</summary>
    public ReadOnlySpan<uint> RawFormIDs => _formIds.AsSpan(0, Count);

    private PluginRecordIndex(ModKey modKey, GameRelease release, MasterStyle masterStyle, IReadOnlyList<ModKey> masters, int capacity, int editorIdCapacity)
    {
        ModKey = modKey;
        Release = release;
        MasterStyle = masterStyle;
        Masters = masters;
        _formIds = new uint[capacity];
        _recordTypes = new int[capacity];
        _flags = new int[capacity];
        _positions = new long[capacity];
        _parents = new int[capacity];
        _editorIdStarts = new int[capacity];
        _editorIdLengths = new ushort[capacity];
        _editorIdPool = new byte[editorIdCapacity];
    }

    /// <summary>A record's type.</summary>
    public RecordType GetRecordType(int record) => new(Checked(record, _recordTypes));

    /// <summary>A record's header flags.</summary>
    public int MajorRecordFlags(int record) => Checked(record, _flags);

    /// <summary>Where a record's header starts in the plugin file.</summary>
    public long Position(int record) => Checked(record, _positions);

    /// <summary>The record whose children group holds this one, or -1 for a record outside any.</summary>
    public int Parent(int record) => Checked(record, _parents);

    /// <summary>A record's EditorID as stored (without its terminator), or empty when it has none.</summary>
    public ReadOnlySpan<byte> EditorIDBytes(int record)
    {
        var start = Checked(record, _editorIdStarts);
        return start < 0 ? ReadOnlySpan<byte>.Empty : _editorIdPool.AsSpan(start, _editorIdLengths[record]);
    }

    /// <summary>A record's EditorID, or null when it has none.</summary>
    public string? EditorID(int record)
    {
        if (Checked(record, _editorIdStarts) < 0) return null;
        return GameConstants.Get(Release).Encodings.NonTranslated.GetString(EditorIDBytes(record));
    }

    private T Checked<T>(int record, T[] values)
    {
        if ((uint)record >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(record));
        return values[record];
    }

    #region Reading plugins

    /// <summary>Indexes a plugin file. A plugin on the real file system is memory-mapped rather than read.</summary>
    /// <param name="parallel">Bounds the parallelism a large plugin is scanned with.</param>
    public static PluginRecordIndex FromPath(ModPath path, GameRelease release, IFileSystem? fileSystem = null, ParallelOptions? parallel = null)
    {
        if (fileSystem != null && fileSystem is not FileSystem)
        {
            return FromBytes(fileSystem.File.ReadAllBytes(path), path.ModKey, release, parallel);
        }
        var length = new FileInfo(path).Length;
        if (length == 0) throw new InvalidDataException($"{path.ModKey} is empty.");
        using var file = MemoryMappedFile.CreateFromFile(
            File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
        using var view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        unsafe
        {
            byte* pointer = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            try
            {
                return Scan(pointer + view.PointerOffset, checked((int)length), path.ModKey, release, parallel, SplitThreshold);
            }
            finally
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
    }

    /// <summary>Indexes a plugin held in memory.</summary>
    /// <param name="parallel">Bounds the parallelism a large plugin is scanned with.</param>
    public static PluginRecordIndex FromBytes(ReadOnlySpan<byte> bytes, ModKey modKey, GameRelease release, ParallelOptions? parallel = null) =>
        FromBytes(bytes, modKey, release, parallel, SplitThreshold);

    internal static PluginRecordIndex FromBytes(ReadOnlySpan<byte> bytes, ModKey modKey, GameRelease release, ParallelOptions? parallel, int splitThreshold)
    {
        unsafe
        {
            fixed (byte* pointer = bytes)
            {
                return Scan(pointer, bytes.Length, modKey, release, parallel, splitThreshold);
            }
        }
    }

    private static unsafe PluginRecordIndex Scan(byte* pointer, int length, ModKey modKey, GameRelease release, ParallelOptions? parallel, int splitThreshold)
    {
        var meta = GameConstants.Get(release);
        var bytes = new ReadOnlySpan<byte>(pointer, length);
        if (length < meta.ModHeaderLength) throw new InvalidDataException($"{modKey} is too short to have a header.");
        var headerLength = meta.ModHeaderLength + (int)U32(bytes, 4);
        if (headerLength > length) throw new InvalidDataException($"{modKey}'s header runs past the end of the file.");
        var header = new ModHeaderFrame(meta, new ReadOnlyMemorySlice<byte>(bytes[..headerLength].ToArray()));
        var masters = MasterReferenceCollection.FromModHeader(modKey, header).Masters.Select(m => m.Master).ToArray();
        var scanner = new Scanner(meta, pointer, length);
        var index = length < splitThreshold
            ? scanner.Whole(modKey, header.MasterStyle, masters, headerLength)
            : scanner.Split(modKey, header.MasterStyle, masters, headerLength, parallel ?? new ParallelOptions());
        index.Trim();
        return index;
    }

    private static uint U32(ReadOnlySpan<byte> bytes, int position) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[position..]);

    /// <summary>
    /// Walks a plugin's groups. Positions are ints: a plugin is memory-mapped as one span, which limits it to 2 GiB.
    /// The header layouts are those of <see cref="MajorRecordHeader"/> and <see cref="GroupHeader"/>: type, then
    /// length, then flags (a group's label) and FormID (a group's type); their lengths come from the game's constants.
    /// </summary>
    private sealed unsafe class Scanner(GameConstants meta, byte* pointer, int length)
    {
        /// <summary>A piece's records whose parent is outside the piece (an exterior block's cells), fixed when joined.</summary>
        private const int OutsideParent = -2;

        private readonly int _recordHeader = meta.MajorConstants.HeaderLength;
        private readonly int _groupHeader = meta.GroupConstants.HeaderLength;
        private readonly int _subrecordHeader = meta.SubConstants.HeaderLength;
        private readonly int _worldChildren = meta.GroupConstants.World.TopGroupType;
        private readonly int[] _worldCellGroups = meta.GroupConstants.World.CellGroupTypes;
        private readonly bool[] _labelsParent = LabelsParent(meta.GroupConstants);

        private ReadOnlySpan<byte> Bytes => new(pointer, length);

        /// <summary>The group types whose label is the FormID of the record their records belong to.</summary>
        private static bool[] LabelsParent(GroupConstants groups)
        {
            var types = new List<int> { groups.World.TopGroupType, groups.Cell.TopGroupType };
            if (groups.Topic != null) types.Add(groups.Topic.TopGroupType);
            if (groups.Quest != null) types.Add(groups.Quest.TopGroupType);
            var labels = new bool[types.Max() + 1];
            foreach (var type in types) labels[type] = true;
            return labels;
        }

        public PluginRecordIndex Whole(ModKey modKey, MasterStyle style, ModKey[] masters, int start)
        {
            var index = new PluginRecordIndex(modKey, meta.Release, style, masters, Math.Max(16, length / 200), Math.Max(256, length / 16));
            Contents(index, Bytes, start, length, parent: -1);
            return index;
        }

        /// <summary>
        /// A large plugin in pieces scanned in parallel: each top-level group, except that a worldspace group is walked
        /// here (its worldspace records, and what their children groups hold outside the exterior cell blocks) and each
        /// exterior cell block is a piece, whose cells' parent is the worldspace. The head (what was walked here) and
        /// the pieces are then joined, in file order within each: the head first.
        /// </summary>
        public PluginRecordIndex Split(ModKey modKey, MasterStyle style, ModKey[] masters, int start, ParallelOptions parallel)
        {
            var bytes = Bytes;
            var pieces = new List<(int Start, int End, int Parent)>();
            var head = new PluginRecordIndex(modKey, meta.Release, style, masters, 1024, 16384);
            for (var position = start; position < length;)
            {
                var size = GroupSize(bytes, position);
                if (U32(bytes, position + 8) == (uint)RecordTypes.WRLD.TypeInt)
                {
                    Worldspaces(head, bytes, position + _groupHeader, position + size, pieces);
                }
                else
                {
                    pieces.Add((position + _groupHeader, position + size, -1));
                }
                position += size;
            }

            var built = new PluginRecordIndex[pieces.Count];
            var perPiece = Math.Max(16, length / 200 / Math.Max(1, pieces.Count));
            Parallel.For(0, pieces.Count, parallel, p =>
            {
                var (pieceStart, pieceEnd, parent) = pieces[p];
                var index = new PluginRecordIndex(modKey, meta.Release, style, masters, perPiece, perPiece * 16);
                Contents(index, Bytes, pieceStart, pieceEnd, parent < 0 ? -1 : OutsideParent);
                built[p] = index;
            });

            var parts = built.Prepend(head).ToArray();
            var bases = new int[parts.Length + 1];
            var poolBases = new int[parts.Length + 1];
            for (var i = 0; i < parts.Length; i++)
            {
                bases[i + 1] = bases[i] + parts[i].Count;
                poolBases[i + 1] = poolBases[i] + parts[i]._editorIdPoolLength;
            }
            var joined = new PluginRecordIndex(modKey, meta.Release, style, masters, bases[^1], poolBases[^1])
            {
                Count = bases[^1],
                _editorIdPoolLength = poolBases[^1],
            };
            Parallel.For(0, parts.Length, parallel, i =>
            {
                var part = parts[i];
                var at = bases[i];
                var count = part.Count;
                part._formIds.AsSpan(0, count).CopyTo(joined._formIds.AsSpan(at));
                part._recordTypes.AsSpan(0, count).CopyTo(joined._recordTypes.AsSpan(at));
                part._flags.AsSpan(0, count).CopyTo(joined._flags.AsSpan(at));
                part._positions.AsSpan(0, count).CopyTo(joined._positions.AsSpan(at));
                part._editorIdLengths.AsSpan(0, count).CopyTo(joined._editorIdLengths.AsSpan(at));
                part._editorIdPool.AsSpan(0, part._editorIdPoolLength).CopyTo(joined._editorIdPool.AsSpan(poolBases[i]));
                // The head comes first, so a piece's worldspace keeps its index in the head.
                var outside = i == 0 ? -1 : pieces[i - 1].Parent;
                for (var r = 0; r < count; r++)
                {
                    var parent = part._parents[r];
                    joined._parents[at + r] = parent == OutsideParent ? outside : parent < 0 ? -1 : parent + at;
                    var editorId = part._editorIdStarts[r];
                    joined._editorIdStarts[at + r] = editorId < 0 ? -1 : editorId + poolBases[i];
                }
            });
            return joined;
        }

        /// <summary>
        /// A top-level worldspace group's contents: worldspace records, and their children groups' contents outside the
        /// cell blocks (the persistent cell and its children, roads), go to the head; each cell block is a piece.
        /// </summary>
        private void Worldspaces(PluginRecordIndex head, ReadOnlySpan<byte> bytes, int position, int end, List<(int Start, int End, int Parent)> pieces)
        {
            var world = -1;
            while (position < end)
            {
                if (U32(bytes, position) != (uint)Constants.Group.TypeInt)
                {
                    world = Add(head, bytes, position, parent: -1);
                    position += RecordSize(bytes, position);
                    continue;
                }
                var size = GroupSize(bytes, position);
                var parent = (int)U32(bytes, position + 12) == _worldChildren ? world : -1;
                for (var inner = position + _groupHeader; inner < position + size;)
                {
                    if (U32(bytes, inner) != (uint)Constants.Group.TypeInt)
                    {
                        Add(head, bytes, inner, parent);
                        inner += RecordSize(bytes, inner);
                        continue;
                    }
                    var innerSize = GroupSize(bytes, inner);
                    if (parent >= 0 && Array.IndexOf(_worldCellGroups, (int)U32(bytes, inner + 12)) >= 0)
                    {
                        pieces.Add((inner + _groupHeader, inner + innerSize, parent));
                    }
                    else
                    {
                        Contents(head, bytes, inner, inner + innerSize, parent);
                    }
                    inner += innerSize;
                }
                position += size;
            }
        }

        /// <summary>A run of records and groups; a group whose label names a parent gives its records that parent.</summary>
        private void Contents(PluginRecordIndex index, ReadOnlySpan<byte> bytes, int position, int end, int parent)
        {
            while (position < end)
            {
                if (U32(bytes, position) != (uint)Constants.Group.TypeInt)
                {
                    Add(index, bytes, position, parent);
                    position += RecordSize(bytes, position);
                    continue;
                }
                var size = GroupSize(bytes, position);
                var groupType = (int)U32(bytes, position + 12);
                var childParent = groupType < _labelsParent.Length && _labelsParent[groupType]
                    ? index.FindLabel(U32(bytes, position + 8))
                    : parent;
                Contents(index, bytes, position + _groupHeader, position + size, childParent);
                position += size;
            }
        }

        private int RecordSize(ReadOnlySpan<byte> bytes, int position)
        {
            if (position + _recordHeader > length) throw new InvalidDataException($"A record header at {position} runs past the end of the file.");
            var size = _recordHeader + (long)U32(bytes, position + 4);
            if (position + size > length) throw new InvalidDataException($"The record at {position} runs past the end of the file.");
            return (int)size;
        }

        private int GroupSize(ReadOnlySpan<byte> bytes, int position)
        {
            if (position + _groupHeader > length) throw new InvalidDataException($"A group header at {position} runs past the end of the file.");
            var size = (long)U32(bytes, position + 4);
            if (size < _groupHeader || position + size > length) throw new InvalidDataException($"The group at {position} has an invalid length.");
            return (int)size;
        }

        private int Add(PluginRecordIndex index, ReadOnlySpan<byte> bytes, int position, int parent)
        {
            var size = RecordSize(bytes, position);
            var flags = (int)U32(bytes, position + 8);
            var i = index.Append(U32(bytes, position + 12), (int)U32(bytes, position), flags, position, parent);
            var content = bytes.Slice(position + _recordHeader, size - _recordHeader);
            Span<byte> buffer = stackalloc byte[_subrecordHeader + 512];
            ReadOnlySpan<byte> first;
            if ((flags & Constants.CompressedFlag) != 0)
            {
                // The decompressed length, then zlib: only the first subrecord is decompressed.
                if (content.Length < 4) return i;
                var zlib = content[4..];
                var got = Inflate(zlib, buffer[.._subrecordHeader]);
                if (got < _subrecordHeader || U32(buffer, 0) != (uint)RecordTypes.EDID.TypeInt) return i;
                var want = Math.Min(_subrecordHeader + BinaryPrimitives.ReadUInt16LittleEndian(buffer[4..]), buffer.Length);
                got = Inflate(zlib, buffer[..want]);
                first = buffer[..Math.Max(0, got)];
            }
            else
            {
                first = content;
            }
            if (first.Length < _subrecordHeader || U32(first, 0) != (uint)RecordTypes.EDID.TypeInt) return i;
            var text = first.Slice(_subrecordHeader, Math.Min(BinaryPrimitives.ReadUInt16LittleEndian(first[4..]), first.Length - _subrecordHeader));
            var terminator = text.IndexOf((byte)0);
            if (terminator >= 0) text = text[..terminator];
            index.SetEditorID(i, text);
            return i;
        }

        /// <summary>The start of a zlib stream; the full decoder when the prefix decoder rejects it.</summary>
        private static int Inflate(ReadOnlySpan<byte> zlib, Span<byte> output)
        {
            var got = InflatePrefix.Inflate(zlib, output);
            if (got >= 0) return got;
            try
            {
                using var stream = new ZLibStream(new MemoryStream(zlib.ToArray()), CompressionMode.Decompress);
                return stream.ReadAtLeast(output, output.Length, throwOnEndOfStream: false);
            }
            catch (InvalidDataException)
            {
                return -1;
            }
        }
    }

    private int _editorIdPoolLength;

    private int Append(uint formId, int recordType, int flags, long position, int parent)
    {
        if (Count == _formIds.Length) Grow();
        var i = Count++;
        _formIds[i] = formId;
        _recordTypes[i] = recordType;
        _flags[i] = flags;
        _positions[i] = position;
        _parents[i] = parent;
        _editorIdStarts[i] = -1;
        return i;
    }

    private void SetEditorID(int record, ReadOnlySpan<byte> text)
    {
        if (_editorIdPoolLength + text.Length > _editorIdPool.Length)
        {
            Array.Resize(ref _editorIdPool, Math.Max(_editorIdPool.Length * 2, _editorIdPoolLength + text.Length));
        }
        text.CopyTo(_editorIdPool.AsSpan(_editorIdPoolLength));
        _editorIdStarts[record] = _editorIdPoolLength;
        _editorIdLengths[record] = (ushort)text.Length;
        _editorIdPoolLength += text.Length;
    }

    /// <summary>The record a children group is labelled with: normally one of the last few read.</summary>
    private int FindLabel(uint formId)
    {
        for (var i = Count - 1; i >= 0 && i >= Count - 4; i--)
        {
            if (_formIds[i] == formId) return i;
        }
        return Count == 0 ? -1 : Array.LastIndexOf(_formIds, formId, Count - 1);
    }

    private void Grow()
    {
        var size = Math.Max(16, _formIds.Length * 2);
        Array.Resize(ref _formIds, size);
        Array.Resize(ref _recordTypes, size);
        Array.Resize(ref _flags, size);
        Array.Resize(ref _positions, size);
        Array.Resize(ref _parents, size);
        Array.Resize(ref _editorIdStarts, size);
        Array.Resize(ref _editorIdLengths, size);
    }

    private void Trim()
    {
        if (_formIds.Length != Count)
        {
            Array.Resize(ref _formIds, Count);
            Array.Resize(ref _recordTypes, Count);
            Array.Resize(ref _flags, Count);
            Array.Resize(ref _positions, Count);
            Array.Resize(ref _parents, Count);
            Array.Resize(ref _editorIdStarts, Count);
            Array.Resize(ref _editorIdLengths, Count);
        }
        if (_editorIdPool.Length != _editorIdPoolLength) Array.Resize(ref _editorIdPool, _editorIdPoolLength);
    }

    #endregion

    #region Saving

    /// <summary>
    /// Writes the index, to be read back with <see cref="TryReadFrom"/> instead of scanning the plugin again. Knowing
    /// whether the plugin has changed since (its size and last write time, say) is the caller's.
    /// </summary>
    public void WriteTo(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(FormatMagic);
        writer.Write(FormatVersion);
        writer.Write((int)Release);
        writer.Write(ModKey.FileName.String);
        writer.Write((int)MasterStyle);
        writer.Write(Masters.Count);
        foreach (var master in Masters) writer.Write(master.FileName.String);
        writer.Write(Count);
        writer.Write(_editorIdPoolLength);
        WriteArray(writer, _formIds.AsSpan(0, Count));
        WriteArray(writer, _recordTypes.AsSpan(0, Count));
        WriteArray(writer, _flags.AsSpan(0, Count));
        WriteArray(writer, _positions.AsSpan(0, Count));
        WriteArray(writer, _parents.AsSpan(0, Count));
        WriteArray(writer, _editorIdStarts.AsSpan(0, Count));
        WriteArray(writer, _editorIdLengths.AsSpan(0, Count));
        WriteArray(writer, _editorIdPool.AsSpan(0, _editorIdPoolLength));
    }

    /// <summary>
    /// Reads an index written by <see cref="WriteTo"/>. Returns false, rather than throwing, for anything that is not a
    /// complete index in this version's format, so that the caller scans the plugin instead.
    /// </summary>
    public static bool TryReadFrom(Stream stream, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out PluginRecordIndex index)
    {
        index = null;
        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (!reader.ReadBytes(FormatMagic.Length).AsSpan().SequenceEqual(FormatMagic) || reader.ReadInt32() != FormatVersion) return false;
            var release = (GameRelease)reader.ReadInt32();
            if (!Enum.IsDefined(release) || !ModKey.TryFromFileName(reader.ReadString(), out var modKey)) return false;
            var style = (MasterStyle)reader.ReadInt32();
            var masters = new ModKey[reader.ReadInt32()];
            for (var m = 0; m < masters.Length; m++)
            {
                if (!ModKey.TryFromFileName(reader.ReadString(), out masters[m])) return false;
            }
            var count = reader.ReadInt32();
            var pool = reader.ReadInt32();
            if (count < 0 || pool < 0) return false;
            var read = new PluginRecordIndex(modKey, release, style, masters, count, pool)
            {
                Count = count,
                _editorIdPoolLength = pool,
            };
            if (!ReadArray(stream, read._formIds) || !ReadArray(stream, read._recordTypes) || !ReadArray(stream, read._flags)
                || !ReadArray(stream, read._positions) || !ReadArray(stream, read._parents) || !ReadArray(stream, read._editorIdStarts)
                || !ReadArray(stream, read._editorIdLengths) || !ReadArray(stream, read._editorIdPool))
            {
                return false;
            }
            for (var r = 0; r < count; r++)
            {
                var start = read._editorIdStarts[r];
                if (read._parents[r] < -1 || read._parents[r] >= count
                    || start < -1 || start >= 0 && start + read._editorIdLengths[r] > pool)
                {
                    return false;
                }
            }
            index = read;
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    private static void WriteArray<T>(BinaryWriter writer, ReadOnlySpan<T> values) where T : unmanaged
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Saved record indexes are little-endian.");
        writer.Write(MemoryMarshal.AsBytes(values));
    }

    private static bool ReadArray<T>(Stream stream, T[] values) where T : unmanaged
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Saved record indexes are little-endian.");
        var bytes = MemoryMarshal.AsBytes(values.AsSpan());
        return stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) == bytes.Length;
    }

    #endregion
}
