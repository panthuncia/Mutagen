using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Utility;
using Mutagen.Bethesda.Strings;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Streams;

/// <summary>
/// Class containing all the extra meta bits for writing
/// </summary>
public sealed record WritingBundle(GameConstants Constants)
{
    /// <summary>
    /// Game constants meta object to reference for header length measurements
    /// </summary>
    public GameConstants Constants { get; } = Constants;

    /// <summary>
    /// Optional master references for easy access during write operations
    /// </summary>
    public IReadOnlyMasterReferenceCollection? MasterReferences { get; set; }

    internal IReadOnlySeparatedMasterPackage? SeparatedMasterPackage { get; set; }

    /// <summary>
    /// Optional strings writer for easy access during write operations
    /// </summary>
    public StringsWriter? StringsWriter { get; set; }

    /// <summary>
    /// Optional RecordInfoCache to reference while reading
    /// </summary>
    public RecordTypeInfoCacheReader? RecordInfoCache { get; set; }

    /// <summary>
    /// Tracker of current major record version
    /// </summary>
    public ushort? FormVersion { get; set; }

    /// <summary>
    /// Mod header HEDR version
    /// </summary>
    public float? ModHeaderVersion { get; set; }

    /// <summary>
    /// If a FormID has all zeros for the ID, but a non-zero mod index, then set mod index to zero as well.
    /// </summary>
    public bool CleanNulls { get; set; } = true;
    
    public Language? TargetLanguageOverride { get; set; }

    public EncodingBundle Encodings { get; set; } = Constants.Encodings;
    
    public IModFlagsGetter? Header { get; set; }

    /// <summary>
    /// A bundle for writing a mod's records one at a time, outside a write of the whole mod. FormKeys are mapped to
    /// FormIDs against the mod's master list, as writing the whole mod maps them.
    /// </summary>
    /// <param name="mod">The mod the records belong to. Its master list must include every mod the records link to.</param>
    /// <param name="masterFlagLookup">
    /// Required for games with separated master load orders (e.g. Starfield).
    /// Can be null for legacy games (e.g. Skyrim, Oblivion, Fallout 4).
    /// </param>
    public static WritingBundle ForRecordsOf(
        IModGetter mod,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagLookup = null)
    {
        var masters = new MasterReferenceCollection(mod.ModKey, mod.MasterReferences);
        return new WritingBundle(GameConstants.Get(mod.GameRelease))
        {
            MasterReferences = masters,
            SeparatedMasterPackage = Masters.SeparatedMasterPackage.Factory(
                mod.GameRelease,
                mod.ModKey,
                mod.GetMasterStyle(),
                masters,
                masterFlagLookup),
            Header = mod,
        };
    }
}