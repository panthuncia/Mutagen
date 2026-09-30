using System.Text;
using Loqui;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

/// <summary>
/// The byte-level index against Mutagen: every plugin's versions, as <c>EnumerateMajorRecordBatches</c> yields them, have
/// the same FormKeys, record types, EditorIDs and parents in the prototype (built fresh, or from the cache folder given);
/// and the EditorID index finds each record's winning EditorID, the last one in load order, in any letter case.
/// <code>SafePatch.Bench index-check &lt;Data folder&gt; &lt;plugins.txt or .paths&gt; [cache folder]</code>
/// </summary>
internal static class IndexCheck
{
    public static int Run(string data, string pluginsTxt, string? cache)
    {
#if SAFEPATCH_BATCHES
        var paths = Conflicts.Paths(data, pluginsTxt);
        var (index, _) = IndexPrototype.Build(paths.Select(p => p.Path.Path).ToArray(), cache);
        var encoding = Encoding.GetEncoding(1252);
        var failures = new SortedDictionary<string, int>(StringComparer.Ordinal);
        long compared = 0;
        var winners = new System.Collections.Concurrent.ConcurrentDictionary<FormKey, (int Plugin, string EditorId)>();
        void Fail(string what, string example) { if (!failures.ContainsKey(what)) Console.WriteLine($"  {what}: e.g. {example}"); failures[what] = failures.GetValueOrDefault(what) + 1; }
        FormKey KeyOf(IndexPrototype.PluginIndex plugin, int r)
        {
            var key = plugin.Key(r);
            return new FormKey(ModKey.FromFileName(index.Mods[(int)(key >> 24)]), (uint)(key & 0xFFFFFF));
        }
        Parallel.For(0, paths.Count, p =>
        {
            using var mod = SkyrimMod.CreateFromBinaryOverlay(paths[p], SkyrimRelease.SkyrimSE);
            var plugin = index.Plugins[p];
            var byKey = new Dictionary<FormKey, int>();
            for (var r = 0; r < plugin.Count; r++) byKey[KeyOf(plugin, r)] = r;
            var seen = 0;
            foreach (var entry in mod.EnumerateMajorRecordBatches().SelectMany(b => b))
            {
                seen++;
                var record = entry.Record;
                lock (failures) compared++;
                if (!byKey.TryGetValue(record.FormKey, out var r)) { lock (failures) Fail("missing from the index", $"{record.FormKey} in {plugin.FileName}"); continue; }
                var signature = Encoding.ASCII.GetString(BitConverter.GetBytes(plugin.Signatures[r]));
                var expectedSignature = ((ILoquiObject)record).Registration.ClassType.Name;
                var editorId = plugin.EditorIdStart[r] < 0 ? null : encoding.GetString(plugin.EditorIdPool, plugin.EditorIdStart[r], plugin.EditorIdLength[r]);
                if (editorId != record.EditorID && !(string.IsNullOrEmpty(editorId) && string.IsNullOrEmpty(record.EditorID)))
                    lock (failures) Fail("EditorID", $"{record.FormKey} {expectedSignature}: index \"{editorId}\", Mutagen \"{record.EditorID}\"");
                if (!string.IsNullOrEmpty(record.EditorID))
                    winners.AddOrUpdate(record.FormKey, (p, record.EditorID), (_, old) => old.Plugin > p ? old : (p, record.EditorID));
                var parent = plugin.Parents[r] < 0 ? (FormKey?)null : KeyOf(plugin, plugin.Parents[r]);
                if (parent != entry.Parent?.FormKey)
                    lock (failures) Fail("parent", $"{record.FormKey} {expectedSignature} ({signature}): index {parent}, Mutagen {entry.Parent?.FormKey}");
            }
            if (seen != plugin.Count) lock (failures) Fail("record count", $"{plugin.FileName}: index {plugin.Count}, Mutagen {seen}");
        });
        var editorIds = IndexPrototype.EditorIdIndex.Build(index);
        if (editorIds.Count != winners.Count) Fail("EditorID index count", $"index {editorIds.Count}, Mutagen {winners.Count}");
        Parallel.ForEach(winners, w =>
        {
            foreach (var query in new[] { w.Value.EditorId, w.Value.EditorId.ToUpperInvariant(), w.Value.EditorId.ToLowerInvariant() })
            {
                var chain = editorIds.Find(query);
                var found = chain < 0 ? null : encoding.GetString(IndexPrototype.EditorIdIndex.Winner(index, chain));
                if (!string.Equals(found, w.Value.EditorId, StringComparison.OrdinalIgnoreCase))
                    lock (failures) Fail("EditorID lookup", $"\"{query}\" ({w.Key}): found \"{found}\"");
            }
        });
        Console.WriteLine($"{winners.Count:N0} winning EditorIDs looked up in three letter cases");
        Console.WriteLine($"{compared:N0} versions compared; differing: " + (failures.Count == 0 ? "none" : string.Join(", ", failures.Select(f => $"{f.Key} {f.Value}"))));
        return failures.Count == 0 ? 0 : 1;
#else
        Console.Error.WriteLine("index-check needs EnumerateMajorRecordBatches: build with -p:SafePatchBatches=true.");
        return 2;
#endif
    }
}
