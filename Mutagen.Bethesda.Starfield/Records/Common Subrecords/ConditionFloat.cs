using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Translations.Binary;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

partial class ConditionFloatBinaryOverlay
{
    public float ComparisonValue => FloatBinaryTranslation<MutagenFrame, MutagenWriter>.Instance.GetFloat(_structData.Slice(4));
}

partial class ConditionFloatBinaryCreateTranslation
{
    public static partial void CustomBinaryEndImport(
        MutagenFrame frame,
        IConditionFloat obj)
    {
        ConditionBinaryCreateTranslation.CustomStringImports(frame.Reader, obj.Data);
    }
}

partial class ConditionFloatBinaryWriteTranslation
{
    public static partial void CustomBinaryEndExport(
        MutagenWriter writer,
        IConditionFloatGetter obj)
    {
        CustomStringExports(writer, obj.Data);
    }
}