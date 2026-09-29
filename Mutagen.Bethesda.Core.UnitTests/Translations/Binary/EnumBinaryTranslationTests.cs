using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Translations.Binary;
using Noggog;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Translations.Binary;

public class EnumBinaryTranslationTests
{
    [Flags]
    public enum TwoByteFlag
    {
        Low = 0x0001,
        High = 0x8000,
    }

    private static MutagenFrame Frame(params byte[] bytes) =>
        new(new MutagenInterfaceReadStream(
            new BinaryReadStream(new MemoryStream(bytes)),
            new ParsingMeta(GameConstants.SkyrimSE, ModKey.Null, SeparatedMasterPackage.EmptyNull)));

    private static EnumBinaryTranslation<TwoByteFlag, MutagenFrame, MutagenWriter> Translation =>
        EnumBinaryTranslation<TwoByteFlag, MutagenFrame, MutagenWriter>.Instance;

    [Fact]
    public void TwoByteFlagsAreNotSignExtended()
    {
        // 0x8001 read as a signed short is -32767, a value no combination of the enum's flags has.
        Translation.Parse(Frame(0x01, 0x80), 2).ShouldBe(TwoByteFlag.High | TwoByteFlag.Low);
    }

    [Fact]
    public void TwoByteFlagsAreNotSignExtendedFromSpans()
    {
        // The overlay reads through this; the package is not used for a plain value.
        Translation.Parse(0, new byte[] { 0x01, 0x80 }, package: default, enumLength: 2).ShouldBe(TwoByteFlag.High | TwoByteFlag.Low);
    }
}
