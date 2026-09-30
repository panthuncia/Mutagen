using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;
using SafePatch.Authoring.Index;

/// <summary>
/// A load-order index built from the plugins' bytes, without Mutagen: every record version's FormKey, signature, flags,
/// file offset, parent (the record whose children group holds it) and EditorID, in flat arrays, then every FormKey's
/// versions in load order by one sort. Plugins are memory-mapped, so nothing is copied; records are never parsed
/// beyond their headers and, for the EditorID, their first subrecord (inflating only that far when compressed).
/// <code>SafePatch.Bench index-proto &lt;Data folder&gt; &lt;plugins.txt or .paths&gt; [runs=3] [label]</code>
/// </summary>
internal static unsafe class IndexPrototype
{
    private const uint Grup = 0x50555247, Edid = 0x44494445, Tes4 = 0x34534554, Mast = 0x5453414D;
    private const uint Compressed = 0x40000;

    /// <summary>One plugin's records, in file order.</summary>
    public sealed class PluginIndex
    {
        public required string FileName;
        public required int[] MasterMods;   // global mod index of each master, then of the plugin itself
        public int Count;
        public uint[] FormIds = [];
        public uint[] Signatures = [];
        public uint[] Flags = [];
        public long[] Offsets = [];
        public int[] Parents = [];          // index of the parent record in this plugin, or -1
        public int[] EditorIdStart = [];    // into EditorIdPool, or -1
        public ushort[] EditorIdLength = [];
        public byte[] EditorIdPool = [];

        public ulong Key(int record)
        {
            var formId = FormIds[record];
            var master = (int)(formId >> 24);
            var mod = master < MasterMods.Length - 1 ? MasterMods[master] : MasterMods[^1];
            return ((ulong)mod << 24) | (formId & 0xFFFFFF);
        }
    }

    /// <summary>The whole load order: plugins, and every FormKey's versions (indexes into Versions) in load order.</summary>
    public sealed class LoadOrderIndex
    {
        public required string[] Mods;               // global mod index -> file name
        public required PluginIndex[] Plugins;       // load order
        public required int[] PluginStart;           // first global version of each plugin
        public required ulong[] SortedKeys;          // one per chain
        public required int[] ChainStart;            // into Versions; ChainStart[^1] == Versions.Length
        public required int[] Versions;              // global version ids, grouped by key, in load order
        public int Overridden;
    }

    public static int Run(string data, string pluginsTxt, int runs, string label)
    {
        var paths = Conflicts.Paths(data, pluginsTxt).Select(p => p.Path.Path).ToArray();
        for (var run = 0; run < runs; run++)
        {
            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var clock = Stopwatch.StartNew();
            var (index, scanTime) = Build(paths);
            var total = clock.Elapsed;
            var versions = index.Versions.Length;
            Console.WriteLine($"run {run + 1}: {index.Plugins.Length} plugins, {versions:N0} versions, {index.SortedKeys.Length:N0} records, " +
                              $"{index.Overridden:N0} overridden: scan {scanTime.TotalMilliseconds:N0} ms, grouping {(total - scanTime).TotalMilliseconds:N0} ms, " +
                              $"total {total.TotalMilliseconds:N0} ms; {(GC.GetTotalAllocatedBytes(precise: true) - allocated) / 1048576:N0} MiB allocated");
            Console.WriteLine($"INDEX|{label}|{run + 1}|{versions}|{index.Overridden}|{scanTime.TotalMilliseconds:F0}|{(total - scanTime).TotalMilliseconds:F0}|{total.TotalMilliseconds:F0}");
            GC.KeepAlive(index);
        }
        return 0;
    }

    public static (LoadOrderIndex Index, TimeSpan ScanTime) Build(IReadOnlyList<string> paths)
    {
        var clock = Stopwatch.StartNew();
        var mods = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int Mod(string name)
        {
            lock (mods)
            {
                if (!mods.TryGetValue(name, out var index)) mods[name] = index = mods.Count;
                return index;
            }
        }
        foreach (var path in paths) Mod(Path.GetFileName(path));
        var plugins = new PluginIndex[paths.Count];
        // Largest first, so the one big plugin does not start last.
        var order = Enumerable.Range(0, paths.Count).OrderByDescending(i => new FileInfo(paths[i]).Length).ToArray();
        Parallel.ForEach(order, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i => plugins[i] = Scan(paths[i], Mod));
        var scanTime = clock.Elapsed;

        // One sort puts each FormKey's versions together, in load order: key in the high bits, version id below.
        var pluginStart = new int[plugins.Length + 1];
        for (var p = 0; p < plugins.Length; p++) pluginStart[p + 1] = pluginStart[p] + plugins[p].Count;
        var total = pluginStart[^1];
        const int VersionBits = 24;
        if (total >= 1 << VersionBits) throw new NotSupportedException("Too many versions for the prototype's packing.");
        var packed = new ulong[total];
        Parallel.For(0, plugins.Length, p =>
        {
            var plugin = plugins[p];
            for (var r = 0; r < plugin.Count; r++) packed[pluginStart[p] + r] = (plugin.Key(r) << VersionBits) | (uint)(pluginStart[p] + r);
        });
        SortByOwningMod(packed, mods.Count, VersionBits);

        var keys = new List<ulong>(total);
        var chainStart = new List<int>(total + 1);
        var versions = new int[total];
        var overridden = 0;
        for (var i = 0; i < total; i++)
        {
            var key = packed[i] >> VersionBits;
            versions[i] = (int)(packed[i] & ((1u << VersionBits) - 1));
            if (i == 0 || key != keys[^1])
            {
                if (i > 0 && i - chainStart[^1] > 1) overridden++;
                keys.Add(key);
                chainStart.Add(i);
            }
        }
        if (total > 0 && total - chainStart[^1] > 1) overridden++;
        chainStart.Add(total);
        var names = new string[mods.Count];
        foreach (var (name, index) in mods) names[index] = name;
        return (new LoadOrderIndex
        {
            Mods = names, Plugins = plugins, PluginStart = pluginStart, SortedKeys = [.. keys], ChainStart = [.. chainStart],
            Versions = versions, Overridden = overridden,
        }, scanTime);
    }

    /// <summary>Buckets by the owning mod (the key's top bits), then sorts each bucket, in parallel.</summary>
    private static void SortByOwningMod(ulong[] packed, int modCount, int versionBits)
    {
        var shift = 24 + versionBits;
        var counts = new int[modCount + 1];
        foreach (var value in packed) counts[(int)(value >> shift) + 1]++;
        for (var m = 0; m < modCount; m++) counts[m + 1] += counts[m];
        var starts = (int[])counts.Clone();
        var bucketed = new ulong[packed.Length];
        foreach (var value in packed) bucketed[starts[(int)(value >> shift)]++] = value;
        Parallel.For(0, modCount, m => bucketed.AsSpan(counts[m], counts[m + 1] - counts[m]).Sort());
        bucketed.CopyTo(packed, 0);
    }

    public static PluginIndex Scan(string path, Func<string, int> mod)
    {
        using var file = MemoryMappedFile.CreateFromFile(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
        using var view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        byte* pointer = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        try
        {
            var length = new FileInfo(path).Length;
            var bytes = new ReadOnlySpan<byte>(pointer + view.PointerOffset, checked((int)length));
            return ScanBytes(Path.GetFileName(path), bytes, mod);
        }
        finally
        {
            view.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    private static PluginIndex ScanBytes(string fileName, ReadOnlySpan<byte> bytes, Func<string, int> mod)
    {
        // Header: masters, to turn stored FormIDs into FormKeys.
        if (U32(bytes, 0) != Tes4) throw new InvalidDataException($"{fileName} has no TES4 header.");
        var headerSize = (int)U32(bytes, 4);
        var masters = new List<int>();
        for (var pos = 24; pos < 24 + headerSize;)
        {
            var sig = U32(bytes, pos);
            var len = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(pos + 4)..]);
            if (sig == Mast) masters.Add(mod(Encoding.ASCII.GetString(bytes.Slice(pos + 6, len).TrimEnd((byte)0))));
            pos += 6 + len;
        }
        masters.Add(mod(fileName));
        var builder = new Builder(fileName, [.. masters], bytes.Length / 200);
        builder.Contents(bytes, 24 + headerSize, bytes.Length, parent: -1);
        return builder.Finish();
    }

    private sealed class Builder(string fileName, int[] masterMods, int capacity)
    {
        private readonly PluginIndex _index = new()
        {
            FileName = fileName, MasterMods = masterMods,
            FormIds = new uint[capacity], Signatures = new uint[capacity], Flags = new uint[capacity], Offsets = new long[capacity],
            Parents = new int[capacity], EditorIdStart = new int[capacity], EditorIdLength = new ushort[capacity], EditorIdPool = new byte[capacity * 16],
        };
        private int _pool;

        /// <summary>A group's contents: records, and groups whose records belong to the parent the group names.</summary>
        public void Contents(ReadOnlySpan<byte> bytes, int pos, int end, int parent)
        {
            while (pos < end)
            {
                var type = U32(bytes, pos);
                var size = (int)U32(bytes, pos + 4);
                if (type == Grup)
                {
                    var groupType = (int)U32(bytes, pos + 12);
                    var childParent = parent;
                    // World children (1), cell children (6), topic children (7), cell persistent/temporary/distant (8-10):
                    // their records' parent is the record the group is labelled with, which precedes the group.
                    if (groupType is 1 or 6 or 7)
                    {
                        childParent = Find(U32(bytes, pos + 8));
                    }
                    Contents(bytes, pos + 24, pos + size, childParent);
                    pos += size;
                    continue;
                }
                Add(bytes, pos, type, size, parent);
                pos += 24 + size;
            }
        }

        /// <summary>The record a children group is labelled with: normally the last record read.</summary>
        private int Find(uint formId)
        {
            for (var i = _index.Count - 1; i >= 0 && i >= _index.Count - 4; i--)
            {
                if (_index.FormIds[i] == formId) return i;
            }
            return Array.LastIndexOf(_index.FormIds, formId, _index.Count - 1);
        }

        private void Add(ReadOnlySpan<byte> bytes, int pos, uint type, int size, int parent)
        {
            if (_index.Count == _index.FormIds.Length) Grow();
            var i = _index.Count++;
            var flags = U32(bytes, pos + 8);
            _index.FormIds[i] = U32(bytes, pos + 12);
            _index.Signatures[i] = type;
            _index.Flags[i] = flags;
            _index.Offsets[i] = pos;
            _index.Parents[i] = parent;
            _index.EditorIdStart[i] = -1;
            var data = bytes.Slice(pos + 24, size);
            Span<byte> head = stackalloc byte[6 + 512];
            ReadOnlySpan<byte> sub;
            if ((flags & Compressed) != 0)
            {
                if (data.Length < 4) return;
                var got = InflatePrefix.Inflate(data[4..], head[..6]);
                if (got < 6 || U32(head, 0) != Edid) return;
                var want = Math.Min(6 + BinaryPrimitives.ReadUInt16LittleEndian(head[4..]), head.Length);
                got = InflatePrefix.Inflate(data[4..], head[..want]);
                if (got < 6) return;
                sub = head[..got];
            }
            else
            {
                sub = data;
            }
            if (sub.Length < 6 || U32(sub, 0) != Edid) return;
            var len = Math.Min(BinaryPrimitives.ReadUInt16LittleEndian(sub[4..]), sub.Length - 6);
            var text = sub.Slice(6, len);
            var zero = text.IndexOf((byte)0);
            if (zero >= 0) text = text[..zero];
            if (_pool + text.Length > _index.EditorIdPool.Length) Array.Resize(ref _index.EditorIdPool, Math.Max(_index.EditorIdPool.Length * 2, _pool + text.Length));
            text.CopyTo(_index.EditorIdPool.AsSpan(_pool));
            _index.EditorIdStart[i] = _pool;
            _index.EditorIdLength[i] = (ushort)text.Length;
            _pool += text.Length;
        }

        private void Grow()
        {
            var size = Math.Max(16, _index.FormIds.Length * 2);
            Array.Resize(ref _index.FormIds, size);
            Array.Resize(ref _index.Signatures, size);
            Array.Resize(ref _index.Flags, size);
            Array.Resize(ref _index.Offsets, size);
            Array.Resize(ref _index.Parents, size);
            Array.Resize(ref _index.EditorIdStart, size);
            Array.Resize(ref _index.EditorIdLength, size);
        }

        public PluginIndex Finish() => _index;
    }

    private static uint U32(ReadOnlySpan<byte> bytes, int pos) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[pos..]);
}