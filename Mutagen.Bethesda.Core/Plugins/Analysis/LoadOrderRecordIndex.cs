using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Text;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Strings.DI;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Analysis;

/// <summary>One version of a record: a record of one of the index's plugins.</summary>
/// <param name="Plugin">The plugin's place in the load order.</param>
/// <param name="Record">The record's index in that plugin's <see cref="PluginRecordIndex"/>.</param>
public readonly record struct RecordVersion(int Plugin, int Record);

/// <summary>
/// Every record of a load order, and each record's versions in load order (the last is the winner), from the
/// plugins' <see cref="PluginRecordIndex"/>es: the stored FormIDs become FormKeys through each plugin's masters, and
/// one sort groups every FormKey's versions. Records are numbered 0 to <see cref="Count"/>, in FormKey order within
/// the plugin that defines them.
/// </summary>
public sealed class LoadOrderRecordIndex
{
    private readonly ModKey[] _mods;
    private readonly Dictionary<ModKey, int> _modIndexes;
    private readonly Func<uint, ulong>[] _keyOf;
    private readonly ulong[] _keys;
    private readonly int[] _versionStarts;
    private readonly RecordVersion[] _versions;
    private readonly IMutagenEncoding _encoding;
    private EditorIdLookup? _editorIds;

    /// <summary>The plugins, in load order.</summary>
    public IReadOnlyList<PluginRecordIndex> Plugins { get; }

    /// <summary>How many records (FormKeys) the load order holds.</summary>
    public int Count => _keys.Length;

    /// <summary>How many records have more than one version.</summary>
    public int OverriddenCount { get; }

    private LoadOrderRecordIndex(IReadOnlyList<PluginRecordIndex> plugins, ModKey[] mods, Dictionary<ModKey, int> modIndexes,
        Func<uint, ulong>[] keyOf, ulong[] keys, int[] versionStarts, RecordVersion[] versions, int overridden)
    {
        Plugins = plugins;
        _mods = mods;
        _modIndexes = modIndexes;
        _keyOf = keyOf;
        _keys = keys;
        _versionStarts = versionStarts;
        _versions = versions;
        OverriddenCount = overridden;
        _encoding = plugins.Count == 0 ? MutagenEncoding._1252 : GameConstants.Get(plugins[0].Release).Encodings.NonTranslated;
    }

    /// <summary>A record's FormKey.</summary>
    public FormKey GetFormKey(int record) => ToFormKey(_keys[record]);

    /// <summary>A record's versions, in load order.</summary>
    public ReadOnlySpan<RecordVersion> Versions(int record) => _versions.AsSpan(_versionStarts[record], _versionStarts[record + 1] - _versionStarts[record]);

    /// <summary>A record's winning version: the last in load order.</summary>
    public RecordVersion Winner(int record) => _versions[_versionStarts[record + 1] - 1];

    /// <summary>The FormKey of a plugin's record.</summary>
    public FormKey GetFormKey(RecordVersion version) => ToFormKey(_keyOf[version.Plugin](Plugins[version.Plugin].RawFormIDs[version.Record]));

    /// <summary>The FormKey of the record whose children group holds a version, if any.</summary>
    public FormKey? Parent(RecordVersion version)
    {
        var parent = Plugins[version.Plugin].Parent(version.Record);
        return parent < 0 ? null : GetFormKey(version with { Record = parent });
    }

    /// <summary>Finds a record by FormKey.</summary>
    public bool TryFind(FormKey formKey, out int record)
    {
        record = -1;
        if (!_modIndexes.TryGetValue(formKey.ModKey, out var mod) || formKey.ID > 0xFFFFFF) return false;
        var found = Array.BinarySearch(_keys, ((ulong)mod << 24) | formKey.ID);
        if (found < 0) return false;
        record = found;
        return true;
    }

    /// <summary>
    /// A record's EditorID: that of its last version with one, so that an override without an EditorID keeps the
    /// name the record was given.
    /// </summary>
    public string? EditorID(int record)
    {
        var bytes = EditorIDBytes(record);
        return bytes.IsEmpty ? null : _encoding.GetString(bytes);
    }

    private ReadOnlySpan<byte> EditorIDBytes(int record)
    {
        for (var v = _versionStarts[record + 1] - 1; v >= _versionStarts[record]; v--)
        {
            var version = _versions[v];
            var bytes = Plugins[version.Plugin].EditorIDBytes(version.Record);
            if (!bytes.IsEmpty) return bytes;
        }
        return ReadOnlySpan<byte>.Empty;
    }

    /// <summary>
    /// Finds a record by its EditorID (as <see cref="EditorID(int)"/> gives it), ignoring the case of ASCII letters.
    /// When records share an EditorID, one of them. The lookup is built on first use.
    /// </summary>
    public bool TryFindByEditorID(string editorId, out int record)
    {
        var lookup = LazyInitializer.EnsureInitialized(ref _editorIds, () => new EditorIdLookup(this));
        record = lookup.Find(editorId);
        return record >= 0;
    }

    private FormKey ToFormKey(ulong key) => new(_mods[(int)(key >> 24)], (uint)(key & 0xFFFFFF));

    #region Building

    /// <summary>Scans a load order's plugins in parallel, the largest first, and indexes them.</summary>
    /// <param name="masterFlagsLookup">
    /// For games whose masters' FormIDs depend on the masters' styles, the styles of masters outside the load order;
    /// those of the plugins given are read from their headers.
    /// </param>
    public static LoadOrderRecordIndex FromPaths(
        IReadOnlyList<ModPath> plugins,
        GameRelease release,
        IFileSystem? fileSystem = null,
        ParallelOptions? parallel = null,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagsLookup = null)
    {
        parallel ??= new ParallelOptions();
        var fs = fileSystem.GetOrDefault();
        var indexes = new PluginRecordIndex[plugins.Count];
        var largestFirst = Enumerable.Range(0, plugins.Count).OrderByDescending(i => fs.FileInfo.New(plugins[i]).Length).ToArray();
        // One plugin at a time: an array would be handed out in ranges, the largest plugins together.
        Parallel.ForEach(Partitioner.Create(largestFirst, EnumerablePartitionerOptions.NoBuffering), parallel,
            i => indexes[i] = PluginRecordIndex.FromPath(plugins[i], release, fileSystem, parallel));
        return Create(indexes, parallel, masterFlagsLookup);
    }

    /// <summary>Indexes a load order from its plugins' indexes, given in load order.</summary>
    /// <param name="masterFlagsLookup">
    /// For games whose masters' FormIDs depend on the masters' styles, the styles of masters outside the load order;
    /// those of the plugins given are taken from their indexes.
    /// </param>
    public static LoadOrderRecordIndex Create(
        IReadOnlyList<PluginRecordIndex> plugins,
        ParallelOptions? parallel = null,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagsLookup = null)
    {
        parallel ??= new ParallelOptions();
        if (plugins.Select(p => p.Release).Distinct().Count() > 1) throw new ArgumentException("The plugins were read as different game releases.", nameof(plugins));

        // Every mod a FormID can name: the plugins, then masters outside the load order.
        var modIndexes = new Dictionary<ModKey, int>();
        var mods = new List<ModKey>();
        void AddMod(ModKey modKey)
        {
            if (modIndexes.TryAdd(modKey, mods.Count)) mods.Add(modKey);
        }
        foreach (var plugin in plugins) AddMod(plugin.ModKey);
        foreach (var plugin in plugins)
        {
            foreach (var master in plugin.Masters) AddMod(master);
        }
        if (mods.Count >= 1 << 16) throw new NotSupportedException("A load order index holds at most 65,536 mods.");

        var keyOf = new Func<uint, ulong>[plugins.Count];
        var styles = plugins.Count > 0 && GameConstants.Get(plugins[0].Release).SeparateMasterLoadOrders
            ? StyleLookup(plugins, masterFlagsLookup)
            : null;
        for (var p = 0; p < plugins.Count; p++) keyOf[p] = KeyResolver(plugins[p], modIndexes, styles);

        // One sort puts each FormKey's versions together, in load order: the key (mod and ID) above the version's
        // number, which counts through the plugins in load order.
        var pluginStarts = new int[plugins.Count + 1];
        for (var p = 0; p < plugins.Count; p++) pluginStarts[p + 1] = checked(pluginStarts[p] + plugins[p].Count);
        var total = pluginStarts[^1];
        var versionBits = 64 - 24 - BitsFor(mods.Count);
        if (versionBits < 32 && total >= 1L << versionBits) throw new NotSupportedException("Too many record versions for a load order index.");
        var packed = new ulong[total];
        Parallel.For(0, plugins.Count, parallel, p =>
        {
            var plugin = plugins[p];
            var formIds = plugin.RawFormIDs;
            var resolve = keyOf[p];
            for (var r = 0; r < formIds.Length; r++) packed[pluginStarts[p] + r] = (resolve(formIds[r]) << versionBits) | (uint)(pluginStarts[p] + r);
        });
        SortByMod(packed, mods.Count, 24 + versionBits, parallel);

        var keys = new List<ulong>();
        var versionStarts = new List<int>();
        var versions = new RecordVersion[total];
        var overridden = 0;
        var versionMask = (1UL << versionBits) - 1;
        for (var i = 0; i < total; i++)
        {
            var key = packed[i] >> versionBits;
            if (i == 0 || key != keys[^1])
            {
                if (i > 0 && i - versionStarts[^1] > 1) overridden++;
                keys.Add(key);
                versionStarts.Add(i);
            }
        }
        if (total > 0 && total - versionStarts[^1] > 1) overridden++;
        versionStarts.Add(total);
        Parallel.ForEach(Partitioner.Create(0, total, Math.Max(4096, total / 64)), parallel, range =>
        {
            for (var i = range.Item1; i < range.Item2; i++)
            {
                var version = (int)(packed[i] & versionMask);
                var plugin = Array.BinarySearch(pluginStarts, version);
                // Plugins without records share a start; a version belongs to the last plugin starting at or before it.
                if (plugin < 0) plugin = ~plugin - 1;
                while (pluginStarts[plugin + 1] <= version) plugin++;
                versions[i] = new RecordVersion(plugin, version - pluginStarts[plugin]);
            }
        });
        return new LoadOrderRecordIndex(plugins, mods.ToArray(), modIndexes, keyOf, keys.ToArray(), versionStarts.ToArray(), versions, overridden);
    }

    private static int BitsFor(int count) => count <= 1 ? 1 : 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)(count - 1));

    /// <summary>Buckets by mod (the top bits), then sorts each bucket, in parallel.</summary>
    private static void SortByMod(ulong[] packed, int modCount, int shift, ParallelOptions parallel)
    {
        var counts = new int[modCount + 1];
        foreach (var value in packed) counts[(int)(value >> shift) + 1]++;
        for (var m = 0; m < modCount; m++) counts[m + 1] += counts[m];
        var next = (int[])counts.Clone();
        var bucketed = new ulong[packed.Length];
        foreach (var value in packed) bucketed[next[(int)(value >> shift)]++] = value;
        Parallel.For(0, modCount, parallel, m => bucketed.AsSpan(counts[m], counts[m + 1] - counts[m]).Sort());
        bucketed.CopyTo(packed, 0);
    }

    /// <summary>A plugin's stored FormIDs to keys (the mod's index above the 24-bit ID).</summary>
    private static Func<uint, ulong> KeyResolver(PluginRecordIndex plugin, Dictionary<ModKey, int> modIndexes, IReadOnlyCache<IModMasterStyledGetter, ModKey>? styles)
    {
        if (styles == null)
        {
            // A master index past the masters is the plugin itself.
            var table = plugin.Masters.Append(plugin.ModKey).Select(m => (ulong)modIndexes[m] << 24).ToArray();
            return formId => table[Math.Min(formId >> 24, (uint)table.Length - 1)] | (formId & 0xFFFFFF);
        }
        var masters = new MasterReferenceCollection(plugin.ModKey, plugin.Masters.Select(m => new MasterReference { Master = m }));
        var package = SeparatedMasterPackage.Factory(plugin.Release, plugin.ModKey, plugin.MasterStyle, masters, styles);
        return formId =>
        {
            var formKey = package.GetFormKey(new FormID(formId), reference: false);
            return ((ulong)modIndexes[formKey.ModKey] << 24) | formKey.ID;
        };
    }

    /// <summary>Master styles: the load order's own, and those given for masters outside it.</summary>
    private static Cache<IModMasterStyledGetter, ModKey> StyleLookup(IReadOnlyList<PluginRecordIndex> plugins, IReadOnlyCache<IModMasterStyledGetter, ModKey>? outside)
    {
        var styles = new Cache<IModMasterStyledGetter, ModKey>(m => m.ModKey);
        if (outside != null) styles.Set(outside.Items);
        foreach (var plugin in plugins) styles.Set(new KeyedMasterStyle(plugin.ModKey, plugin.MasterStyle));
        return styles;
    }

    /// <summary>
    /// Every record's EditorID, for lookup ignoring the case of ASCII letters: (hash, record) pairs sorted by hash,
    /// with a 32-bit hash of the case-folded bytes in the top half. A hash match is confirmed against the bytes.
    /// </summary>
    private sealed class EditorIdLookup
    {
        private readonly LoadOrderRecordIndex _index;
        private readonly ulong[] _entries;

        public EditorIdLookup(LoadOrderRecordIndex index)
        {
            _index = index;
            // Each block's entries, then one array of them all, sized from the blocks' counts. (LINQ's ToArray over the
            // blocks would build it from segments rented from the shared ArrayPool, which keeps them afterwards: some
            // 76 MB for a 2-million-record load order, for a 2.5 MB lookup.)
            var block = Math.Max(4096, index.Count / 64);
            var blocks = new ulong[(index.Count + block - 1) / block][];
            Parallel.For(0, blocks.Length, b =>
            {
                var end = Math.Min(index.Count, (b + 1) * block);
                var count = 0;
                for (var record = b * block; record < end; record++)
                {
                    if (!index.EditorIDBytes(record).IsEmpty) count++;
                }
                var local = new ulong[count];
                var at = 0;
                for (var record = b * block; record < end; record++)
                {
                    var bytes = index.EditorIDBytes(record);
                    if (!bytes.IsEmpty) local[at++] = (Hash(bytes) << 32) | (uint)record;
                }
                blocks[b] = local;
            });
            _entries = new ulong[blocks.Sum(b => b.Length)];
            var next = 0;
            foreach (var local in blocks)
            {
                local.CopyTo(_entries, next);
                next += local.Length;
            }
            _entries.AsSpan().Sort();
        }

        public int Find(string editorId)
        {
            if (editorId.Length == 0) return -1;
            var query = new byte[_index._encoding.GetByteCount(editorId)];
            _index._encoding.GetBytes(editorId, query);
            var hash = Hash(query);
            var low = 0;
            var high = _entries.Length;
            while (low < high)
            {
                var middle = (low + high) >>> 1;
                if (_entries[middle] >> 32 < hash) low = middle + 1;
                else high = middle;
            }
            for (var i = low; i < _entries.Length && _entries[i] >> 32 == hash; i++)
            {
                var record = (int)(uint)_entries[i];
                if (Ascii.EqualsIgnoreCase(_index.EditorIDBytes(record), query)) return record;
            }
            return -1;
        }

        /// <summary>FNV-1a over the bytes with ASCII letters lowercased, folded to 32 bits.</summary>
        private static ulong Hash(ReadOnlySpan<byte> text)
        {
            var hash = 14695981039346656037UL;
            foreach (var b in text) hash = (hash ^ (b is >= (byte)'A' and <= (byte)'Z' ? (uint)(b + 32) : b)) * 1099511628211UL;
            return (hash ^ (hash >> 32)) & 0xFFFFFFFF;
        }
    }

    #endregion
}
