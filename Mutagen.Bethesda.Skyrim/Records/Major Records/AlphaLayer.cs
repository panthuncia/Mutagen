using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Translations.Binary;

namespace Mutagen.Bethesda.Skyrim;

public static partial class AlphaLayerMixIn
{
    /// <summary>
    /// The points of an alpha layer's data, as <see cref="IAlphaLayerGetter.AlphaLayerData"/> lists them; read from a
    /// file, they're read from its bytes, without an object for each (a landscape's layers hold thousands).
    /// </summary>
    public static IEnumerable<(ushort Position, float Opacity)> AlphaPoints(this IAlphaLayerGetter layer)
    {
        if (layer.AlphaLayerData is not { } points) yield break;
        if (points is BinaryOverlayList.BinaryOverlayListByStartIndex<IAlphaLayerDataGetter> overlay
            && overlay.ItemLength == 8 && overlay.Memory.Length % 8 == 0)
        {
            var memory = overlay.Memory;
            for (var at = 0; at < memory.Length; at += 8)
            {
                yield return (
                    BinaryPrimitives.ReadUInt16LittleEndian(memory.Span.Slice(at, 2)),
                    FloatBinaryTranslation<MutagenFrame, MutagenWriter>.Instance.GetFloat(memory.Span.Slice(at + 4, 4)));
            }
            yield break;
        }
        foreach (var point in points)
        {
            yield return (point.Position, point.Opacity);
        }
    }
}
