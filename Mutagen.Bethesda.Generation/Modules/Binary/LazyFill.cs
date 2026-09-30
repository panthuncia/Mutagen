using Noggog.StructuredStrings;

namespace Mutagen.Bethesda.Generation.Modules.Binary;

/// <summary>
/// Emits overlay members that a record's fill sets and its getters read, as properties that complete a deferred fill
/// before they are read (<c>PluginBinaryOverlay.EnsureFilled</c>). The fill itself writes through the setters, and
/// its own reads pass straight through, so eager overlays behave exactly as before.
/// </summary>
public static class LazyFill
{
    /// <summary>A private location or state field, e.g. <c>private int? _NameLocation;</c>.</summary>
    public static void Field(StructuredStringBuilder sb, string type, string name)
    {
        sb.AppendLine($"private {type} {name}Store;");
        sb.AppendLine($"private {type} {name} {{ get {{ EnsureFilled(); return {name}Store; }} set => {name}Store = value; }}");
    }

    /// <summary>A property the fill assigns, e.g. <c>public IModelGetter? Model { get; private set; }</c>.</summary>
    public static void Property(StructuredStringBuilder sb, string accessibility, string type, string name, string? initializer = null)
    {
        sb.AppendLine($"private {type} {name}Store{(initializer is null ? "" : $" = {initializer}")};");
        var setter = accessibility == "private" ? "set" : "private set";
        sb.AppendLine($"{accessibility} {type} {name} {{ get {{ EnsureFilled(); return {name}Store; }} {setter} => {name}Store = value; }}");
    }
}
