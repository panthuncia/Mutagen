using System.Diagnostics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

/// <summary>
/// A patcher's use of a mutable link cache (Mutagen issue 229): for every winning NPC, add an override to a patch mod,
/// then resolve the override itself and its links (template, an <c>INpcSpawnGetter</c> link; race, class, voice,
/// outfit, keywords; inventory items, <c>IItemGetter</c> links) through a cache of the load order plus the growing patch. Every resolve checks the patch
/// first. The same resolves through the load order's immutable cache are the control.
/// <code>SafePatch.Bench linkcache &lt;Data folder&gt; &lt;plugins.txt or .paths&gt; [label]</code>
/// </summary>
internal static class LinkCacheBench
{
    public static int Run(string data, string pluginsTxt, string label)
    {
        var mods = Conflicts.Paths(data, pluginsTxt).AsParallel().AsOrdered()
            .Select(path => SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE))
            .ToArray();
        var loadOrder = new LoadOrder<ISkyrimModGetter>(mods);
        var winners = loadOrder.PriorityOrder.Npc().WinningOverrides().ToArray();
        Console.WriteLine($"{mods.Length} plugins, {winners.Length:N0} winning NPCs");

        // The control: the same resolves, the patch left out.
        var immutable = loadOrder.ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>();
        Resolve(winners, npc => npc, immutable, "immutable", label);

        var patch = new SkyrimMod(ModKey.FromFileName("Patch.esp"), SkyrimRelease.SkyrimSE);
        var mutable = loadOrder.ToMutableLinkCache<ISkyrimMod, ISkyrimModGetter>(patch);
        Resolve(winners, npc => patch.Npcs.GetOrAddAsOverride(npc), mutable, "mutable", label);
        return 0;
    }

    private static void Resolve(INpcGetter[] winners, Func<INpcGetter, INpcGetter> add, ILinkCache cache, string kind, string label)
    {
        const int Bucket = 1000;
        long resolves = 0, found = 0;
        var clock = Stopwatch.StartNew();
        var bucketClock = Stopwatch.StartNew();
        var buckets = new List<double>();
        for (var i = 0; i < winners.Length; i++)
        {
            var npc = add(winners[i]);
            void Link<T>(IFormLinkGetter<T> link) where T : class, IMajorRecordGetter
            {
                if (link.IsNull) return;
                resolves++;
                if (link.TryResolve(cache, out _)) found++;
            }
            // Links to the kind of record the patch collects: the NPC itself (has it been patched?) and its template.
            Link(npc.ToLinkGetter());
            Link(npc.Template);
            Link(npc.Race);
            Link(npc.Class);
            Link(npc.Voice);
            Link(npc.DefaultOutfit);
            foreach (var keyword in npc.Keywords ?? []) Link(keyword);
            foreach (var item in npc.Items ?? []) Link(item.Item.Item);
            if ((i + 1) % Bucket == 0)
            {
                buckets.Add(bucketClock.Elapsed.TotalMilliseconds);
                bucketClock.Restart();
            }
        }
        var total = clock.Elapsed.TotalMilliseconds;
        Console.WriteLine($"{kind,-9} {resolves:N0} resolves ({found:N0} found) in {total:N0} ms; per {Bucket:N0} NPCs: {string.Join(", ", buckets.Select(b => b.ToString("N0")))} ms");
        Console.WriteLine($"LINKCACHE|{label}|{kind}|{resolves}|{found}|{total:F0}|{string.Join(";", buckets.Select(b => b.ToString("F0")))}");
    }
}