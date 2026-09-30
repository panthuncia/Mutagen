using Loqui.Generation;
using Noggog;
using Mutagen.Bethesda.Generation.Fields;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;

namespace Mutagen.Bethesda.Generation.Modules.Binary;

public abstract class BinaryTranslationGeneration : TranslationGeneration
{
    public BinaryTranslationModule Module;
    public virtual string Namespace => "Mutagen.Bethesda.Plugins.Binary.Translations.";
    public virtual bool NeedsNamespacePrefix => true;
    public string NamespacePrefix => NeedsNamespacePrefix ? Namespace : string.Empty;

    /// <summary>
    /// The break flags to set when data stops at break <paramref name="first"/>: that break and every later one, since
    /// the fields after each are all absent. The overlay sets them this way, testing each break against the length.
    /// </summary>
    public static string BreaksFrom(string enumName, int first, int count) =>
        string.Join(" | ", Enumerable.Range(first, Math.Max(1, count - first)).Select(i => $"{enumName}.Break{i}"));

    /// <summary>
    /// What an overlay returns for a field its data does not reach (past a break, or unset): the field's declared
    /// default when it has one, as a new object and a full parse have, otherwise <paramref name="fallback"/>.
    /// </summary>
    public static string OverlayDefault(ObjectGeneration objGen, TypeGeneration typeGen, string fallback) =>
        typeGen.HasDefault ? $"{objGen.ObjectName}.{typeGen.Name}Default" : fallback;
    public virtual bool DoErrorMasks => this.Module.DoErrorMasks;

    public delegate TryGet<string> ParamTest(
        ObjectGeneration objGen,
        TypeGeneration typeGen);
    public List<ParamTest> AdditionalWriteParams = new List<ParamTest>();
    public List<ParamTest> AdditionalCopyInParams = new List<ParamTest>();
    public List<ParamTest> AdditionalCopyInRetParams = new List<ParamTest>();
    public abstract Task<int?> ExpectedLength(ObjectGeneration objGen, TypeGeneration typeGen);

    public virtual bool AllowDirectWrite(
        ObjectGeneration objGen,
        TypeGeneration typeGen) => true;
    public virtual bool AllowDirectParse(
        ObjectGeneration objGen,
        TypeGeneration typeGen,
        bool squashedRepeatedList) => true;

    public abstract Task GenerateWrite(
        StructuredStringBuilder sb,
        ObjectGeneration objGen,
        TypeGeneration typeGen,
        Accessor writerAccessor,
        Accessor itemAccessor,
        Accessor errorMaskAccessor,
        Accessor translationAccessor,
        Accessor converterAccessor);

    public abstract string GetTranslatorInstance(TypeGeneration typeGen, bool getter);

    public abstract Task GenerateCopyIn(
        StructuredStringBuilder sb,
        ObjectGeneration objGen,
        TypeGeneration typeGen,
        Accessor readerAccessor,
        Accessor itemAccessor,
        Accessor errorMaskAccessor,
        Accessor translationAccessor);

    public abstract Task GenerateCopyInRet(
        StructuredStringBuilder sb,
        ObjectGeneration objGen,
        TypeGeneration targetGen,
        TypeGeneration typeGen,
        Accessor readerAccessor,
        AsyncMode asyncMode,
        Accessor retAccessor,
        Accessor outItemAccessor,
        Accessor errorMaskAccessor,
        Accessor translationAccessor,
        Accessor converterAccessor,
        bool inline);

    public virtual async Task GenerateWrapperFields(
        StructuredStringBuilder sb,
        ObjectGeneration objGen,
        TypeGeneration typeGen,
        Accessor structDataAccessor,
        Accessor recordDataAccessor,
        int? passedLength,
        string passedLengthAccessor,
        DataType? data = null)
    {
    }

    public virtual async Task GenerateWrapperUnknownLengthParse(
        StructuredStringBuilder sb,
        ObjectGeneration objGen,
        TypeGeneration typeGen,
        Accessor dataAccessor,
        int? passedLength,
        string passedLengthAccessor,
        DataType? data = null)
    {
    }

    public virtual async Task GenerateWrapperCtor(
        StructuredStringBuilder sb,
        ObjectGeneration objGen,
        TypeGeneration typeGen)
    {
    }

    public virtual async Task<int?> GetPassedAmount(ObjectGeneration objGen, TypeGeneration typeGen)
    {
        var data = typeGen.GetFieldData();
        if (!data.HasTrigger)
        {
            return await this.ExpectedLength(objGen, typeGen);
        }
        return null;
    }

    public virtual async Task GenerateWrapperRecordTypeParse(
        StructuredStringBuilder sb,
        ObjectGeneration objGen,
        TypeGeneration typeGen,
        Accessor locationAccessor,
        Accessor packageAccessor,
        Accessor converterAccessor)
    {
        switch (typeGen.GetFieldData().BinaryOverlayFallback)
        {
            case BinaryGenerationType.Normal:
                var data = typeGen.GetFieldData();
                if (data.MarkerType.HasValue)
                {
                    sb.AppendLine($"stream.ReadSubrecord(); // Skip marker");
                }
                sb.AppendLine($"_{typeGen.Name}Location = {locationAccessor};");
                if (data.MarkerType.HasValue)
                {
                    sb.AppendLine($"stream.ReadSubrecord(); // Skip record");
                }
                break;
            case BinaryGenerationType.Custom:
                using (var args = sb.Call(
                           $"{typeGen.Name}CustomParse"))
                {
                    args.AddPassArg($"stream");
                    args.AddPassArg($"finalPos");
                    args.AddPassArg($"offset");
                }
                break;
            case BinaryGenerationType.NoGeneration:
            default:
                return;
        }
    }

    public virtual string GenerateForTypicalWrapper(
        ObjectGeneration objGen,
        TypeGeneration typeGen,
        Accessor dataAccessor,
        Accessor packageAccessor)
    {
        throw new NotImplementedException();
    }
}