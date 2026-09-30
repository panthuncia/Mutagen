using Mutagen.Bethesda.Plugins.Records.Internals;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using System.Buffers.Binary;
using System.IO.Compression;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Noggog;
using System.Diagnostics;
using Mutagen.Bethesda.Assets;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Internals;

namespace Mutagen.Bethesda.Plugins.Records;

public partial interface IMajorRecord : IFormLinkContainer, IAssetLinkContainer, IMajorRecordQueryable
{
    new FormKey FormKey { get; }
        
    /// <summary>
    /// Marker of whether the content is compressed
    /// </summary>
    new bool IsCompressed { get; set; }

    /// <summary>
    /// Marker of whether the content is deleted
    /// </summary>
    new bool IsDeleted { get; set; }

    /// <summary>
    /// Disables the record by setting the RecordFlag to Initially Disabled.
    /// <returns>Returns true if the disable was successful.</returns>
    /// </summary>
    bool Disable();
}
    
public partial interface IMajorRecordInternal
{
    new FormKey FormKey { get; set; }
}

public partial interface IMajorRecordGetter : 
    IFormVersionGetter, 
    IMajorRecordIdentifierGetter,
    IFormLinkContainerGetter,
    IAssetLinkContainerGetter,
    IFormLinkIdentifier,
    IEquatable<IFormLinkGetter>,
    IMajorRecordQueryableGetter
{
    /// <summary>
    /// Marker of whether the content is compressed
    /// </summary>
    bool IsCompressed { get; }

    /// <summary>
    /// Marker of whether the content is deleted
    /// </summary>
    bool IsDeleted { get; }

    /// <summary>
    /// Form Version of the record
    /// </summary>
    new ushort? FormVersion { get; }
}

[DebuggerDisplay("{GetType().Name} {this.EditorID?.ToString()} {this.FormKey.ToString()}")]
public partial class MajorRecord : IFormLinkContainer
{
    #region EditorID
    public virtual String? EditorID { get; set; }
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    String? IMajorRecordGetter.EditorID => EditorID;
    #endregion

    /// <summary>
    /// A convenience property to print "EditorID - FormKey"
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string TitleString => $"{EditorID} - {FormKey}";

    public bool IsCompressed
    {
        get => Enums.HasFlag(MajorRecordFlagsRaw, Constants.CompressedFlag);
        set => MajorRecordFlagsRaw = Enums.SetFlag(MajorRecordFlagsRaw, Constants.CompressedFlag, value);
    }

    public bool IsDeleted
    {
        get => Enums.HasFlag(MajorRecordFlagsRaw, Constants.DeletedFlag);
        set => MajorRecordFlagsRaw = Enums.SetFlag(MajorRecordFlagsRaw, Constants.DeletedFlag, value);
    }

    protected abstract ushort? FormVersionAbstract { get; }
    ushort? IMajorRecordGetter.FormVersion => FormVersionAbstract;
    ushort? IFormVersionGetter.FormVersion => FormVersionAbstract;

    public virtual bool Disable()
    {
        if (IsDeleted) return false;
        MajorRecordFlagsRaw = Enums.SetFlag(MajorRecordFlagsRaw, (int)Constants.InitiallyDisabled, true);
        return true;
    }

    #region Comparers
    public static IEqualityComparer<IMajorRecordGetter> FormKeyEqualityComparer => _formKeyEqualityComparer;

    private static readonly MajorRecordFormKeyComparer _formKeyEqualityComparer = new();

    class MajorRecordFormKeyComparer : IEqualityComparer<IMajorRecordGetter>
    {
        public bool Equals(IMajorRecordGetter? x, IMajorRecordGetter? y)
        {
            return x?.FormKey == y?.FormKey;
        }

        public int GetHashCode(IMajorRecordGetter obj)
        {
            return obj.FormKey.GetHashCode();
        }
    }
    #endregion

    public bool Equals(IFormLinkGetter? other)
    {
        if (other == null) return false;
        return other.Equals(this);
    }

    Type ILinkIdentifier.Type => LinkType;
    protected abstract Type LinkType { get; }
}

public static class IMajorRecordGetterExt
{
    public static FormLinkInformation ToFormLinkInformation(this IMajorRecordGetter majorRec)
    {
        return FormLinkInformation.Factory(majorRec);
    }
    
    public static bool IsInjected(this IMajorRecordGetter majorRec, ILinkCache linkCache)
    {
        return !linkCache.TryResolveSimpleContext(majorRec, out var context, target: ResolveTarget.Origin)
            || context.ModKey != majorRec.FormKey.ModKey;
    }
}
    
[DebuggerDisplay("{GetType().Name} {this.EditorID?.ToString()} {this.FormKey.ToString()}")]
internal abstract partial class MajorRecordBinaryOverlay : IMajorRecordGetter
{
    public bool IsCompressed => Enums.HasFlag(MajorRecordFlagsRaw, Constants.CompressedFlag);
    public bool IsDeleted => Enums.HasFlag(MajorRecordFlagsRaw, Constants.DeletedFlag);

    /// <summary>Enough of a compressed record to reach an EditorID in its first subrecord.</summary>
    private const int EditorIDPeekLength = 1024;

    /// <summary>
    /// While a fill is still deferred, reads the EditorID straight from the first subrecord, where records keep it,
    /// inflating a compressed record only as far as that. A record whose first subrecord is not an EDID has none, as the
    /// plugin format keeps it first. Returns false (and the caller completes the fill) when the EDID does not fit what
    /// was read.
    /// </summary>
    internal bool TryPeekEditorID(out string? editorId)
    {
        editorId = null;
        var constants = _package.MetaData.Constants;
        // The deferred state holds the record as read; if the fill finished meanwhile, the caller reads it normally.
        if (PendingFill is not { } deferred) return false;
        var raw = deferred.Record.Slice(constants.MajorConstants.HeaderLength);
        var data = raw;
        var headerLength = constants.SubConstants.HeaderLength;
        if (IsCompressed)
        {
            if (data.Length < 4) return false;
            var uncompressed = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data.Span), EditorIDPeekLength);
            // Inflate only the first subrecord's header, and the rest only for an EDID: most compressed records
            // (landscape, navmeshes) have none. The prefix decoder needs no zlib state; zlib is the fallback.
            Span<byte> head = stackalloc byte[headerLength];
            var got = InflatePrefix.Inflate(raw.Slice(4).Span, head[..Math.Min(headerLength, uncompressed)]);
            if (got >= 0)
            {
                if (got < headerLength) return false;
                if (BinaryPrimitives.ReadInt32LittleEndian(head) != RecordTypes.EDID.TypeInt) return true;
                var prefix = new byte[Math.Min(headerLength + BinaryPrimitives.ReadUInt16LittleEndian(head[4..]), uncompressed)];
                got = InflatePrefix.Inflate(raw.Slice(4).Span, prefix);
                if (got >= 0)
                {
                    data = new ReadOnlyMemorySlice<byte>(prefix, 0, got);
                }
            }
            if (got < 0)
            {
                var prefix = new byte[uncompressed];
                try
                {
                    using var zlib = new ZLibStream(new ByteMemorySliceStream(raw.Slice(4)), CompressionMode.Decompress);
                    var read = zlib.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
                    data = new ReadOnlyMemorySlice<byte>(prefix, 0, read);
                }
                catch (InvalidDataException)
                {
                    return false;
                }
            }
        }
        if (data.Length < headerLength) return false;
        var header = constants.SubrecordHeader(data);
        // An EDID, when a record has one, is its first subrecord: a record starting with anything else has none.
        if (header.RecordType != RecordTypes.EDID) return true;
        if (data.Length < headerLength + header.ContentLength) return false;
        editorId = BinaryStringUtility.ProcessWholeToZString(data.Slice(headerLength, header.ContentLength), _package.MetaData.Encodings.NonTranslated);
        return true;
    }

    protected abstract ushort? FormVersionAbstract { get; }
    ushort? IMajorRecordGetter.FormVersion => FormVersionAbstract;
    ushort? IFormVersionGetter.FormVersion => FormVersionAbstract;

    Type ILinkIdentifier.Type => LinkType;
    protected abstract Type LinkType { get; }

    public bool Equals(IFormLinkGetter? other)
    {
        if (other == null) return false;
        return other.Equals(this);
    }
}