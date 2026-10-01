#if SAFEPATCH_RECORD_INDEX && SAFEPATCH_RECORD_READER
using System.Diagnostics;
using Loqui;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

/// <summary>
/// Every record of the plugins, read alone by <c>PluginRecordReader</c> at the position the record index gives, against
/// the same record read by an overlay of the whole plugin: equal, apart from the records a cell, worldspace or topic
/// holds, which the reader doesn't read (those compare by EditorID). Also times the reads.
/// <code>SafePatch.Bench reader-check &lt;Data folder&gt; &lt;plugin,...&gt;</code>
/// </summary>
internal static class ReaderCheck
{
    public static int Run(string data, IReadOnlyList<string> plugins)
    {
        long compared = 0;
        var failures = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var plugin in plugins)
        {
            var path = new ModPath(Path.Combine(data, plugin));
            var index = LoadOrderRecordIndex.Create([PluginRecordIndex.FromPath(path, GameRelease.SkyrimSE)]);
            var positions = new Dictionary<FormKey, long>();
            for (var r = 0; r < index.Plugins[0].Count; r++) positions[index.GetFormKey(new RecordVersion(0, r))] = index.Plugins[0].Position(r);
            using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
            using var reader = PluginRecordReader.FromPath(path, GameRelease.SkyrimSE);
            var records = overlay.EnumerateMajorRecords().ToArray();
            var types = records.Select(r => ((ILoquiObject)r).Registration.GetterType).ToArray();

            var clock = Stopwatch.StartNew();
            var read = new IMajorRecordGetter[records.Length];
            Parallel.For(0, records.Length, i => read[i] = reader.Read(positions[records[i].FormKey], types[i]));
            var readTime = clock.Elapsed;

            Parallel.For(0, records.Length, i =>
            {
                var expected = records[i];
                var actual = read[i];
                var same = expected is ICellGetter or IWorldspaceGetter or IDialogTopicGetter
                    ? actual.EditorID == expected.EditorID && actual.FormKey == expected.FormKey
                    : actual.Equals(expected);
                lock (failures)
                {
                    compared++;
                    if (!same)
                    {
                        var key = types[i].Name;
                        if (!failures.ContainsKey(key)) Console.WriteLine($"  differs: {expected.FormKey} {key}");
                        failures[key] = failures.GetValueOrDefault(key) + 1;
                    }
                }
            });
            Console.WriteLine($"{plugin}: {records.Length:N0} records read alone in {readTime.TotalMilliseconds:N0} ms " +
                              $"({readTime.TotalMicroseconds / Math.Max(1, records.Length):N1} µs each, on every core)");
        }
        Console.WriteLine($"{compared:N0} records compared; differing: " + (failures.Count == 0 ? "none" : string.Join(", ", failures.Select(f => $"{f.Key} {f.Value}"))));
        return failures.Count == 0 ? 0 : 1;
    }
}
#endif
