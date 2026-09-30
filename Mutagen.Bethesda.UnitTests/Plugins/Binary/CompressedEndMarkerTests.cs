using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing;
using Noggog.IO;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

/// <summary>
/// A record whose fields end with an end marker (Starfield's furniture and terminals end with STOP) keeps the marker
/// inside its compressed content when it is compressed, and so reads back.
/// </summary>
public class CompressedEndMarkerTests
{
    private static IMajorRecord[] Records(StarfieldMod mod) =>
    [
        mod.Furniture.AddNew("CompressedFurniture"),
        mod.Terminals.AddNew("CompressedTerminal"),
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompressedRecordsWithAnEndMarkerReadBack(bool parallel)
    {
        using var folder = TempFolder.Factory();
        var path = Path.Combine(folder.Dir, TestConstants.PluginModKey.FileName);
        var mod = new StarfieldMod(TestConstants.PluginModKey, StarfieldRelease.Starfield);
        var records = Records(mod);
        foreach (var record in records) record.IsCompressed = true;
        mod.WriteToBinary(path, new BinaryWriteParameters { Parallel = new ParallelWriteParameters { MaxDegreeOfParallelism = parallel ? -1 : 1 } });

        using (var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield))
        {
            var read = overlay.EnumerateMajorRecords().ToDictionary(r => r.EditorID!);
            foreach (var record in records)
            {
                read[record.EditorID!].IsCompressed.ShouldBeTrue();
                read[record.EditorID!].FormKey.ShouldBe(record.FormKey);
            }
        }
        var full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield);
        var fullRead = full.EnumerateMajorRecords().ToDictionary(r => r.EditorID!);
        foreach (var record in records)
        {
            // Equal to the record written, apart from the compressed flag.
            var copy = (IMajorRecord)fullRead[record.EditorID!].DeepCopy();
            copy.IsCompressed = false;
            record.IsCompressed = false;
            copy.Equals(record).ShouldBeTrue(record.EditorID);
            record.IsCompressed = true;
        }
    }
}
