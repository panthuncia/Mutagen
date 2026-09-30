#if SAFEPATCH_RECORD_INDEX
using System.Collections.Concurrent;
using System.Diagnostics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Analysis;

/// <summary>
/// Mutagen's record index (<c>PluginRecordIndex</c> and <c>LoadOrderRecordIndex</c>, from the prototype in
/// IndexPrototype.cs) over a load order: scanned, or loaded from a cache folder of one saved index per plugin, kept
/// while the plugin's size and last write time are unchanged, as an application would keep them.
/// <code>SafePatch.Bench record-index &lt;Data folder&gt; &lt;plugins.txt or .paths&gt; [runs=3] [label] [cache folder]</code>
/// </summary>
internal static class RecordIndexBench
{
    public static int Run(string data, string pluginsTxt, int runs, string label, string? cache)
    {
        var paths = Conflicts.Paths(data, pluginsTxt).Select(p => p.Path).ToArray();
        for (var run = 0; run < runs; run++)
        {
            GC.Collect();
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var clock = Stopwatch.StartNew();
            var (index, pluginTime) = Build(paths, cache);
            var built = clock.Elapsed;
            clock.Restart();
            index.TryFindByEditorID("PlayerRef", out _);
            var editorIdTime = clock.Elapsed;
            Console.WriteLine($"run {run + 1}: {index.Plugins.Count} plugins, {index.Plugins.Sum(p => p.Count):N0} versions, {index.Count:N0} records, " +
                              $"{index.OverriddenCount:N0} overridden: plugins {pluginTime.TotalMilliseconds:N0} ms ({(cache == null ? "scanned" : "cache or scan")}), " +
                              $"grouping {(built - pluginTime).TotalMilliseconds:N0} ms, first EditorID lookup {editorIdTime.TotalMilliseconds:N0} ms; " +
                              $"total {(built + editorIdTime).TotalMilliseconds:N0} ms; {(GC.GetTotalAllocatedBytes(precise: true) - allocated) / 1048576:N0} MiB allocated");
            Console.WriteLine($"RECINDEX|{label}|{run + 1}|{pluginTime.TotalMilliseconds:F0}|{(built - pluginTime).TotalMilliseconds:F0}|{editorIdTime.TotalMilliseconds:F0}|{(built + editorIdTime).TotalMilliseconds:F0}");
            GC.KeepAlive(index);
        }
        return 0;
    }

    /// <summary>The load order's index, and how long its plugins' indexes took (scanned, or loaded from the cache).</summary>
    public static (LoadOrderRecordIndex Index, TimeSpan PluginTime) Build(IReadOnlyList<ModPath> paths, string? cache)
    {
        var clock = Stopwatch.StartNew();
        var plugins = new PluginRecordIndex[paths.Count];
        var largestFirst = Enumerable.Range(0, paths.Count).OrderByDescending(i => new FileInfo(paths[i]).Length).ToArray();
        Parallel.ForEach(Partitioner.Create(largestFirst, EnumerablePartitionerOptions.NoBuffering),
            i => plugins[i] = cache == null ? PluginRecordIndex.FromPath(paths[i], GameRelease.SkyrimSE) : LoadOrScan(cache, paths[i]));
        var pluginTime = clock.Elapsed;
        return (LoadOrderRecordIndex.Create(plugins), pluginTime);
    }

    private static PluginRecordIndex LoadOrScan(string cache, ModPath path)
    {
        var info = new FileInfo(path);
        var file = Path.Combine(cache, info.Name + ".mrix");
        if (File.Exists(file))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt64() == info.Length && reader.ReadInt64() == info.LastWriteTimeUtc.Ticks && PluginRecordIndex.TryReadFrom(stream, out var saved)) return saved;
        }
        var index = PluginRecordIndex.FromPath(path, GameRelease.SkyrimSE);
        Directory.CreateDirectory(cache);
        using (var stream = new BufferedStream(File.Create(file), 1 << 16))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(info.Length);
            writer.Write(info.LastWriteTimeUtc.Ticks);
            writer.Flush();
            index.WriteTo(stream);
        }
        return index;
    }
}
#endif
