using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Translations.Binary;
using Noggog;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Translations.Binary;

public class EnumBinaryTranslationParseValueTests
{
    public enum Value
    {
        None = 0,
        Two = 2,
    }

    [Theory]
    [InlineData(new byte[] { 2 })]
    [InlineData(new byte[] { 2, 0 })]
    [InlineData(new byte[] { 2, 0, 0, 0 })]
    public void ParseValueReturnsWhatItRead(byte[] bytes)
    {
        var frame = new MutagenFrame(new MutagenInterfaceReadStream(
            new BinaryReadStream(new MemoryStream(bytes)),
            new ParsingMeta(GameConstants.SkyrimSE, ModKey.Null, SeparatedMasterPackage.EmptyNull)));
        EnumBinaryTranslation<Value, MutagenFrame, MutagenWriter>.Instance.ParseValue(frame).ShouldBe(Value.Two);
    }
}
