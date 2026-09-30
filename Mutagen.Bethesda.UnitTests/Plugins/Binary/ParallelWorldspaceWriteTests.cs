using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Testing;
using Noggog.IO;
using Shouldly;
using Xunit;
using Fallout3 = Mutagen.Bethesda.Fallout3;
using Fallout4 = Mutagen.Bethesda.Fallout4;
using Oblivion = Mutagen.Bethesda.Oblivion;
using Skyrim = Mutagen.Bethesda.Skyrim;
using Starfield = Mutagen.Bethesda.Starfield;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

/// <summary>
/// Worldspaces written by a mod's parallel writer (the default) are written as the single-threaded writer writes them:
/// compressed when flagged, so that they read back, and with nothing but their header when deleted.
/// </summary>
public class ParallelWorldspaceWriteTests
{
    /// <summary>A mod with a compressed worldspace holding a persistent cell and reference, and a deleted worldspace.</summary>
    private static (IModGetter Mod, FormKey Compressed) Build(GameRelease release)
    {
        var modKey = TestConstants.PluginModKey;
        switch (release)
        {
            case GameRelease.SkyrimSE:
            {
                var mod = new Skyrim.SkyrimMod(modKey, Skyrim.SkyrimRelease.SkyrimSE);
                var world = mod.Worldspaces.AddNew("CompressedWorld");
                world.TopCell = new Skyrim.Cell(mod, "TopCell") { Persistent = [new Skyrim.PlacedObject(mod, "TopRef")] };
                world.IsCompressed = true;
                mod.Worldspaces.AddNew("DeletedWorld").IsDeleted = true;
                return (mod, world.FormKey);
            }
            case GameRelease.Fallout4:
            {
                var mod = new Fallout4.Fallout4Mod(modKey, Fallout4.Fallout4Release.Fallout4);
                var world = mod.Worldspaces.AddNew("CompressedWorld");
                world.TopCell = new Fallout4.Cell(mod, "TopCell") { Persistent = [new Fallout4.PlacedObject(mod, "TopRef")] };
                world.IsCompressed = true;
                mod.Worldspaces.AddNew("DeletedWorld").IsDeleted = true;
                return (mod, world.FormKey);
            }
            case GameRelease.Starfield:
            {
                var mod = new Starfield.StarfieldMod(modKey, Starfield.StarfieldRelease.Starfield);
                var world = mod.Worldspaces.AddNew("CompressedWorld");
                world.TopCell = new Starfield.Cell(mod, "TopCell") { Persistent = [new Starfield.PlacedObject(mod, "TopRef")] };
                world.IsCompressed = true;
                mod.Worldspaces.AddNew("DeletedWorld").IsDeleted = true;
                return (mod, world.FormKey);
            }
            case GameRelease.Fallout3:
            {
                var mod = new Fallout3.Fallout3Mod(modKey, Fallout3.Fallout3Release.Fallout3);
                var world = mod.Worldspaces.AddNew("CompressedWorld");
                world.TopCell = new Fallout3.Cell(mod, "TopCell") { Persistent = [new Fallout3.PlacedObject(mod, "TopRef")] };
                world.IsCompressed = true;
                mod.Worldspaces.AddNew("DeletedWorld").IsDeleted = true;
                return (mod, world.FormKey);
            }
            case GameRelease.Oblivion:
            {
                var mod = new Oblivion.OblivionMod(modKey, Oblivion.OblivionRelease.Oblivion);
                var world = mod.Worldspaces.AddNew("CompressedWorld");
                world.TopCell = new Oblivion.Cell(mod, "TopCell") { Persistent = [new Oblivion.PlacedObject(mod, "TopRef")] };
                world.IsCompressed = true;
                mod.Worldspaces.AddNew("DeletedWorld").IsDeleted = true;
                return (mod, world.FormKey);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(release));
        }
    }

    [Theory]
    [InlineData(GameRelease.SkyrimSE)]
    [InlineData(GameRelease.Fallout4)]
    [InlineData(GameRelease.Starfield)]
    [InlineData(GameRelease.Fallout3)]
    [InlineData(GameRelease.Oblivion)]
    public void ParallelWriteMatchesSingleThreadedAndReadsBack(GameRelease release)
    {
        using var folder = TempFolder.Factory();
        var (mod, compressed) = Build(release);
        var parallelPath = Path.Combine(folder.Dir, "Parallel", mod.ModKey.FileName);
        var singlePath = Path.Combine(folder.Dir, "Single", mod.ModKey.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(parallelPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(singlePath)!);
        mod.WriteToBinary(parallelPath);
        mod.WriteToBinary(singlePath, new BinaryWriteParameters { Parallel = new ParallelWriteParameters { MaxDegreeOfParallelism = 1 } });

        File.ReadAllBytes(parallelPath).ShouldBe(File.ReadAllBytes(singlePath));

        using var reread = ModFactory.ImportGetter(new ModPath(parallelPath), release);
        var records = reread.EnumerateMajorRecords().ToDictionary(r => r.EditorID ?? r.FormKey.ToString());
        records["CompressedWorld"].FormKey.ShouldBe(compressed);
        records["CompressedWorld"].IsCompressed.ShouldBeTrue();
        records.Keys.ShouldContain("TopCell");
        records.Keys.ShouldContain("TopRef");
        records.Values.Single(r => r.IsDeleted).EditorID.ShouldBeNull();
    }
}
