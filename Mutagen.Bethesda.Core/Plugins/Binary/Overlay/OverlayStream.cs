using Mutagen.Bethesda.Plugins.Binary.Streams;
using Noggog;
using Noggog.Streams.Binary;

namespace Mutagen.Bethesda.Plugins.Binary.Overlay;

/// <summary>
/// The bytes an overlay reads, as a stream: the memory stream itself, rather than a stream wrapping one made with it, as
/// overlays make one for each record and subrecord they read.
/// </summary>
internal sealed class OverlayStream : LittleEndianBinaryMemoryReadStream, IMutagenReadStream
{
    public ParsingMeta MetaData { get; }

    public ReadOnlyMemorySlice<byte> Data { get; }

    public new int Position
    {
        get => PositionInt;
        set => PositionInt = value;
    }

    public new int Length => LengthInt;

    public OverlayStream(ReadOnlyMemorySlice<byte> data, ParsingMeta constants)
        : base(data)
    {
        Data = data;
        MetaData = constants;
    }

    public ReadOnlyMemorySlice<byte> Read(int amount)
    {
        Position += amount;
        return Data.Slice(Position - amount, amount);
    }

    #region IMutagenReadStream
    public long OffsetReference => 0;

    IMutagenReadStream IMutagenReadStream.ReadAndReframe(int length)
    {
        throw new NotImplementedException();
    }
    #endregion
}
