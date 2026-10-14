using System.IO.Compression;
using System.Text;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

public class InflatePrefixTests
{
    private static byte[] Compress(byte[] data, CompressionLevel level)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, level)) zlib.Write(data);
        return output.ToArray();
    }

    public static IEnumerable<object[]> Inputs()
    {
        var random = new Random(1);
        var noise = new byte[5000];
        random.NextBytes(noise);
        // Text compresses with dynamic Huffman blocks, a short run with fixed ones, and noise barely at all.
        var text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 400).Select(i => $"EDID{i % 37}Sword{i}\0")));
        var run = Encoding.ASCII.GetBytes("EDIDIronSword\0");
        foreach (var level in new[] { CompressionLevel.NoCompression, CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.SmallestSize })
        {
            yield return [text, level];
            yield return [run, level];
            yield return [noise, level];
        }
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void EveryPrefixMatchesAFullInflate(byte[] data, CompressionLevel level)
    {
        var compressed = Compress(data, level);
        foreach (var length in new[] { 0, 1, 6, 24, 100, 1000, data.Length })
        {
            var take = Math.Min(length, data.Length);
            var output = new byte[take];
            InflatePrefix.Inflate(compressed, output).ShouldBe(take);
            output.ShouldBe(data.AsSpan(0, take).ToArray());
        }
    }

    [Fact]
    public void RejectsWhatIsNotZlib()
    {
        InflatePrefix.Inflate(new byte[] { 0x12, 0x34, 0x56 }, new byte[10]).ShouldBe(-1);
    }

    [Fact]
    public void DynamicPrefixesAllocateNoScratchOnTheHeap()
    {
        var data = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 400).Select(i => $"EDID{i % 37}Sword{i}\0")));
        var compressed = Compress(data, CompressionLevel.Optimal);
        ((compressed[2] >> 1) & 3).ShouldBe(2);
        var output = new byte[100];
        for (var i = 0; i < 100; i++) InflatePrefix.Inflate(compressed, output);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) InflatePrefix.Inflate(compressed, output);
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
        output.ShouldBe(data.AsSpan(0, output.Length).ToArray());
    }

    [Fact]
    public void ConcurrentMultiBlockInflatesKeepTheirOwnScratch()
    {
        var data = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 10000).Select(i => $"EDID{i % 37}Sword{i}\0")));
        using var bytes = new MemoryStream();
        using (var zlib = new ZLibStream(bytes, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var at = 0; at < data.Length; at += 4000)
            {
                zlib.Write(data.AsSpan(at, Math.Min(4000, data.Length - at)));
                zlib.Flush();
            }
        }
        var compressed = bytes.ToArray();
        Parallel.For(0, 32, i =>
        {
            var length = i % 2 == 0 ? data.Length : 4001;
            var output = new byte[length];
            InflatePrefix.Inflate(compressed, output).ShouldBe(length);
            output.ShouldBe(data.AsSpan(0, length).ToArray());
        });
    }
}
