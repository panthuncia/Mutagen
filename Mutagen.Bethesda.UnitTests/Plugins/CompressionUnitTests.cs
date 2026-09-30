using System.Buffers.Binary;
using System.IO.Abstractions;
using System.IO.Compression;
using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog.Testing.Extensions;

namespace Mutagen.Bethesda.UnitTests.Plugins;

public class CompressionUnitTests
{
    [Theory, MutagenModAutoData]
    public void CompressedExport(
        IFileSystem fileSystem,
        ModPath filePath,
        SkyrimMod mod,
        Npc npc)
    {
        npc.EditorID = "Test123";
        npc.IsCompressed = true;
        mod.BeginWrite
            .ToPath(filePath)
            .WithNoLoadOrder()
            .NoModKeySync()
            .WithFileSystem(fileSystem)
            .Write();
        using var reimport = SkyrimMod.Create(mod.SkyrimRelease)
            .FromPath(filePath)
            .WithFileSystem(fileSystem)
            .Construct();
        reimport.Npcs.Select(x => x.EditorID)
            .ShouldEqualEnumerable("Test123");
    }

    [Fact]
    public void CompressingLeavesTheWritersStreamOpen()
    {
        using var stream = new MemoryStream();
        var writer = new MutagenWriter(stream, GameConstants.SkyrimSE, dispose: false);

        using (CompressionExport.Compression(isCompressed: true, writer, out var compressing))
        {
            compressing.Write(new byte[] { 1, 2, 3, 4 });
        }
        writer.Write(0x0A0B0C0D);

        stream.CanWrite.ShouldBeTrue();
        var written = stream.ToArray();
        BinaryPrimitives.ReadUInt32LittleEndian(written).ShouldBe(4u);
        BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(written.Length - 4)).ShouldBe(0x0A0B0C0Du);
        using var inflating = new ZLibStream(new MemoryStream(written[4..^4]), CompressionMode.Decompress);
        var decompressed = new byte[4];
        inflating.ReadExactly(decompressed);
        decompressed.ShouldBe(new byte[] { 1, 2, 3, 4 });
    }
}