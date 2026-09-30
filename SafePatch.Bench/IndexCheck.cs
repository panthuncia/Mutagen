using Loqui;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
#if SAFEPATCH_RECORD_INDEX
using Mutagen.Bethesda.Plugins.Analysis;
#endif

/// <summary>
/// Mutagen's record index against Mutagen's records: every plugin's versions, as <c>EnumerateMajorRecordBatches</c>
/// yields them, have the same FormKeys, EditorIDs and parents in the index (scanned, or loaded from the cache folder
/// given, as <c>record-index</c> keeps it); each record's EditorID is the last one in load order; and that EditorID
/// finds the record in any letter case.
/// <code>SafePatch.Bench index-check &lt;Data folder&gt; &lt;plugins.txt or .paths&gt; [cache folder]</code>
/// </summary>
internal static class IndexCheck
{
    public static int Run(string data, string pluginsTxt, string? cache)
    {
#if SAFEPATCH_BATCHES && SAFEPATCH_RECORD_INDEX
        var paths = Conflicts.Paths(data, pluginsTxt).ToArray();
        var (index, _) = RecordIndexBench.Build(paths, cache);
        var failures = new SortedDictionary<string, int>(StringComparer.Ordinal);
        long compared = 0;
        void Fail(string what, string example) { if (!failures.ContainsKey(what)) Console.WriteLine($"  {what}: e.g. {example}"); failures[what] = failures.GetValueOrDefault(what) + 1; }
        var winners = new System.Collections.Concurrent.ConcurrentDictionary<FormKey, (int Plugin, string EditorId)>();
        Parallel.For(0, paths.Length, p =>
        {
            using var mod = SkyrimMod.CreateFromBinaryOverlay(paths[p], SkyrimRelease.SkyrimSE);
            var plugin = index.Plugins[p];
            var byKey = new Dictionary<FormKey, int>();
            for (var r = 0; r < plugin.Count; r++) byKey[index.GetFormKey(new RecordVersion(p, r))] = r;
            var seen = 0;
            foreach (var entry in mod.EnumerateMajorRecordBatches().SelectMany(b => b))
            {
                seen++;
                var record = entry.Record;
                lock (failures) compared++;
                if (!byKey.TryGetValue(record.FormKey, out var r)) { lock (failures) Fail("missing from the index", $"{record.FormKey} in {plugin.ModKey}"); continue; }
                var type = ((ILoquiObject)record).Registration.ClassType.Name;
                var editorId = plugin.EditorID(r);
                if (editorId != record.EditorID && !(string.IsNullOrEmpty(editorId) && string.IsNullOrEmpty(record.EditorID)))
                    lock (failures) Fail("EditorID", $"{record.FormKey} {type}: index \"{editorId}\", Mutagen \"{record.EditorID}\"");
                if (!string.IsNullOrEmpty(record.EditorID))
                    winners.AddOrUpdate(record.FormKey, (p, record.EditorID), (_, old) => old.Plugin > p ? old : (p, record.EditorID));
                var parent = index.Parent(new RecordVersion(p, r));
                if (parent != entry.Parent?.FormKey)
                    lock (failures) Fail("parent", $"{record.FormKey} {type} ({plugin.GetRecordType(r)}): index {parent}, Mutagen {entry.Parent?.FormKey}");
            }
            if (seen != plugin.Count) lock (failures) Fail("record count", $"{plugin.ModKey}: index {plugin.Count}, Mutagen {seen}");
        });
        Parallel.ForEach(winners, w =>
        {
            if (!index.TryFind(w.Key, out var byFormKey) || index.EditorID(byFormKey) != w.Value.EditorId)
                lock (failures) Fail("winning EditorID", $"{w.Key}: index \"{(byFormKey >= 0 ? index.EditorID(byFormKey) : null)}\", Mutagen \"{w.Value.EditorId}\"");
            foreach (var query in new[] { w.Value.EditorId, w.Value.EditorId.ToUpperInvariant(), w.Value.EditorId.ToLowerInvariant() })
            {
                var found = index.TryFindByEditorID(query, out var record) ? index.EditorID(record) : null;
                if (!string.Equals(found, w.Value.EditorId, StringComparison.OrdinalIgnoreCase))
                    lock (failures) Fail("EditorID lookup", $"\"{query}\" ({w.Key}): found \"{found}\"");
            }
        });
        Console.WriteLine($"{winners.Count:N0} winning EditorIDs checked and looked up in three letter cases; {index.Count:N0} records, {index.OverriddenCount:N0} overridden");
        Console.WriteLine($"{compared:N0} versions compared; differing: " + (failures.Count == 0 ? "none" : string.Join(", ", failures.Select(f => $"{f.Key} {f.Value}"))));
        return failures.Count == 0 ? 0 : 1;
#else
        Console.Error.WriteLine("index-check needs EnumerateMajorRecordBatches and the record index: build with -p:SafePatchBatches=true -p:SafePatchRecordIndex=true.");
        return 2;
#endif
    }
}
