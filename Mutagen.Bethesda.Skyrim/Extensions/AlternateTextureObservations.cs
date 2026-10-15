using System.Buffers.Binary;
using Mutagen.Bethesda.Skyrim.Internals;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;

namespace Mutagen.Bethesda.Skyrim;

/// <summary>A physical model texture override, decoded by the generated alternate-texture getter.</summary>
public sealed record AlternateTextureObservation(string SubrecordSignature, IAlternateTextureGetter Texture);

/// <summary>Lossless diagnostic access to model texture overrides, including slots without a record-level getter.</summary>
public static class AlternateTextureObservations
{
    /// <summary>Reads all MODS/MO2S/MO3S/MO4S entries in physical order, preserving duplicates and source link context.</summary>
    public static IReadOnlyList<AlternateTextureObservation> Read(ISkyrimMajorRecordGetter record)
    {
        if (record is not PluginBinaryOverlay overlay) throw new ArgumentException("An on-disk binary overlay is required.", nameof(record));
        var data = overlay._recordData;
        var result = new List<AlternateTextureObservation>();
        for (var at = 0; at < data.Length;)
        {
            if (data.Length - at < 6) throw new InvalidDataException("Truncated subrecord header.");
            var signature = BinaryPrimitives.ReadInt32LittleEndian(data.Span.Slice(at, 4));
            var size = (int)BinaryPrimitives.ReadUInt16LittleEndian(data.Span.Slice(at + 4, 2));
            at += 6;
            if (signature == RecordTypeInts.XXXX)
            {
                if (size != 4 || data.Length - at < 10) throw new InvalidDataException("Invalid extended subrecord header.");
                size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Span.Slice(at, 4)));
                signature = BinaryPrimitives.ReadInt32LittleEndian(data.Span.Slice(at + 4, 4));
                at += 10;
            }
            if (size > data.Length - at) throw new InvalidDataException("Subrecord exceeds record data.");
            if (signature is RecordTypeInts.MODS or RecordTypeInts.MO2S or RecordTypeInts.MO3S or RecordTypeInts.MO4S)
            {
                if (size < 4) throw new InvalidDataException("Missing alternate-texture count.");
                var count = BinaryPrimitives.ReadUInt32LittleEndian(data.Span.Slice(at, 4));
                var stream = new OverlayStream(data.Slice(at + 4, size - 4), overlay._package);
                var name = signature == RecordTypeInts.MODS ? "MODS" : signature == RecordTypeInts.MO2S ? "MO2S" : signature == RecordTypeInts.MO3S ? "MO3S" : "MO4S";
                for (uint i = 0; i < count; i++)
                    result.Add(new(name, AlternateTextureBinaryOverlay.AlternateTextureFactory(stream, overlay._package)));
                if (!stream.Complete) throw new InvalidDataException("Trailing alternate-texture data.");
            }
            at += size;
        }
        return result;
    }
}
