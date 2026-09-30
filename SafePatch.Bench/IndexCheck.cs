using System.Text;
using Loqui;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

/// <summary>
/// The byte-level index against Mutagen: every plugin's versions, as <c>EnumerateMajorRecordBatches</c> yields them, have
/// the same FormKeys, record types, EditorIDs and parents in the prototype.
/// <code>SafePatch.Bench index-check &lt;Data folder&gt; &lt;plugins.txt or .paths&gt;</code>
/// </summary>
internal static class IndexCheck
{
    public static int Run(string data, string pluginsTxt)
    {
#if SAFEPATCH_BATCHES
        var paths = Conflicts.Paths(data, pluginsTxt);
        var (index, _) = IndexPrototype.Build(paths.Select(p => p.Path.Path).ToArray());
        var encoding = Encoding.GetEncoding(1252);
        var failures = new SortedDictionary<string, int>(StringComparer.Ordinal);
        long compared = 0;
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
                var parent = plugin.Parents[r] < 0 ? (FormKey?)null : KeyOf(plugin, plugin.Parents[r]);
                if (parent != entry.Parent?.FormKey)
                    lock (failures) Fail("parent", $"{record.FormKey} {expectedSignature} ({signature}): index {parent}, Mutagen {entry.Parent?.FormKey}");
            }
            if (seen != plugin.Count) lock (failures) Fail("record count", $"{plugin.FileName}: index {plugin.Count}, Mutagen {seen}");
        });
        Console.WriteLine($"{compared:N0} versions compared; differing: " + (failures.Count == 0 ? "none" : string.Join(", ", failures.Select(f => $"{f.Key} {f.Value}"))));
        return failures.Count == 0 ? 0 : 1;
#else
        Console.Error.WriteLine("index-check needs EnumerateMajorRecordBatches: build with -p:SafePatchBatches=true.");
        return 2;
#endif
    }
}
