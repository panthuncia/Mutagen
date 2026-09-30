using System.Text.RegularExpressions;
using Loqui.Generation;
using Mutagen.Bethesda.Generation.Modules.Binary;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;
using DictType = Mutagen.Bethesda.Generation.Fields.DictType;
using ObjectType = Mutagen.Bethesda.Plugins.Meta.ObjectType;

namespace Mutagen.Bethesda.Generation.Modules.Plugin;

/// <summary>
/// Generates each mod's <c>EnumerateMajorRecordBatches</c>: every major record that <c>EnumerateMajorRecords</c>
/// yields, each with the major record it is nested in, split into batches that can be read on different threads.
/// Batches split wherever the definitions mark a list <c>thread="true"</c>, the lists binary writing already
/// parallelises: the blocks of interior cells, and each worldspace's blocks of exterior cells. A record holding such a
/// list (a worldspace) is a batch with its other children, and each item of the list is split the same way or is a
/// batch of its own. Every other top-level group is one batch.
/// </summary>
public class MajorRecordBatchesModule : GenerationModule
{
    private const string Entry = nameof(MajorRecordWithParent);
    private const string Batch = $"IEnumerable<{nameof(MajorRecordWithParent)}>";
    private const string Parent = $"{nameof(IMajorRecordGetter)}?";

    public override async Task GenerateInCommonMixin(ObjectGeneration obj, StructuredStringBuilder sb)
    {
        if (obj.GetObjectType() != ObjectType.Mod) return;
        var gen = new Generation();

        var body = new StructuredStringBuilder();
        foreach (var field in obj.IterateFields(includeBaseClass: true))
        {
            await gen.BatchesOfField(body, "obj", field, "null");
        }

        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Every major record, as <c>EnumerateMajorRecords</c> yields them, each with the major record it is nested in,");
        sb.AppendLine("/// split into batches that can be read on different threads: each top-level group, and each block of cells (a");
        sb.AppendLine("/// worldspace is a batch with its persistent cell, and its blocks are batches of their own). A single large mod,");
        sb.AppendLine("/// whose worldspaces hold most of its records, then spreads over every core. Overlay records are safe to read from");
        sb.AppendLine("/// several threads.");
        sb.AppendLine("/// </summary>");
        using (var args = sb.Function($"public static IEnumerable<{Batch}> EnumerateMajorRecordBatches"))
        {
            args.Add($"this {obj.Interface(getter: true)} obj");
        }
        using (sb.CurlyBrace())
        {
            if (body.Count == 0) sb.AppendLine("yield break;");
            else sb.AppendLines(body);
        }
        sb.AppendLine();

        await gen.GenerateHelpers(sb);
    }

    /// <summary>What a field holds, as far as major records go.</summary>
    /// <param name="Accessor">For a single item, the item (possibly null); otherwise the items, enumerable.</param>
    /// <param name="Target">The items' object, or null for a link interface (major records with nothing nested in them).</param>
    /// <param name="TypeName">The items' getter type.</param>
    /// <param name="Threaded">Whether the definitions mark the list <c>thread="true"</c>.</param>
    private sealed record Members(string Accessor, ObjectGeneration? Target, string TypeName, bool Single, bool Threaded);

    private sealed class Generation
    {
        private readonly Dictionary<string, Func<StructuredStringBuilder, Task>> _helpers = new();
        private readonly Queue<string> _pending = new();
        private readonly Dictionary<ObjectGeneration, bool> _splittable = new();
        private int _variables;

        private string Variable() => $"item{_variables++}";

        /// <summary>Emits every batch of a field: the field's records split where the definitions allow.</summary>
        public async Task BatchesOfField(StructuredStringBuilder sb, string owner, TypeGeneration field, string parent)
        {
            if (await MembersOf($"{owner}.{field.Name}", field) is not { } members) return;
            if (members.Single)
            {
                var item = Variable();
                sb.AppendLine($"if ({members.Accessor} is {{}} {item})");
                using (sb.CurlyBrace())
                {
                    if (members.Target != null && await Splittable(members.Target))
                    {
                        await SplitItem(sb, item, members.Target, parent);
                    }
                    else
                    {
                        sb.AppendLine($"yield return {await ItemBatch(members)}({item}, {parent});");
                    }
                }
            }
            else if (members.Target != null && await Splittable(members.Target))
            {
                var item = Variable();
                sb.AppendLine($"foreach (var {item} in {members.Accessor})");
                using (sb.CurlyBrace())
                {
                    await SplitItem(sb, item, members.Target, parent);
                }
            }
            else if (members.Threaded)
            {
                var item = Variable();
                sb.AppendLine($"foreach (var {item} in {members.Accessor})");
                using (sb.CurlyBrace())
                {
                    sb.AppendLine($"yield return {await ItemBatch(members)}({item}, {parent});");
                }
            }
            else
            {
                sb.AppendLine($"yield return {await ListBatch(members)}({members.Accessor}, {parent});");
            }
        }

        /// <summary>
        /// Emits the batches of one item that splits: the item with whatever it holds outside its split fields, then
        /// each split field's batches.
        /// </summary>
        private async Task SplitItem(StructuredStringBuilder sb, string item, ObjectGeneration target, string parent)
        {
            if (await AnyDescendantHoldsRecords(target))
            {
                throw new NotSupportedException($"{target.Name} splits into batches and has subclasses holding records of their own.");
            }
            var isMajor = await target.IsMajorRecord();
            if (isMajor || await HoldsRecords(target, unsplitOnly: true))
            {
                sb.AppendLine($"yield return {Unsplit(target)}({item}, {parent});");
            }
            var innerParent = isMajor ? item : parent;
            foreach (var field in target.IterateFields(includeBaseClass: true))
            {
                if (await MembersOf($"{item}.{field.Name}", field) is { } members && await Splits(members))
                {
                    await BatchesOfField(sb, item, field, innerParent);
                }
            }
        }

        /// <summary>Whether a field's records are split into batches of their own.</summary>
        private async Task<bool> Splits(Members members) =>
            members.Threaded || (members.Target != null && await Splittable(members.Target));

        /// <summary>Whether an object holds, anywhere inside it, a list the definitions mark as threaded.</summary>
        private async Task<bool> Splittable(ObjectGeneration obj)
        {
            if (_splittable.TryGetValue(obj, out var known)) return known;
            _splittable[obj] = false; // a circular reference does not split
            var result = false;
            foreach (var field in obj.IterateFields(includeBaseClass: true))
            {
                if (await MembersOf("_", field) is { } members && await Splits(members))
                {
                    result = true;
                    break;
                }
            }
            return _splittable[obj] = result;
        }

        private static async Task<Members?> MembersOf(string accessor, TypeGeneration field)
        {
            if (field.GetFieldData().Circular) return null;
            switch (field)
            {
                case LoquiType loqui when loqui.TargetObjectGeneration?.GetObjectType() == ObjectType.Group:
                {
                    // A top-level group, or a list group (the interior cell blocks): its records, of the group's type.
                    var target = loqui.GetGroupTarget();
                    var records = loqui.TargetObjectGeneration.IsListGroup()
                        ? loqui.TargetObjectGeneration.IterateFields().OfType<ContainerType>().Single(f => f.Name == "Records")
                        : null;
                    return new Members($"{accessor}.Records", target, target.Interface(getter: true), Single: false, Threaded: records != null && IsThreaded(records));
                }
                case LoquiType loqui:
                {
                    if (await MajorRecordModule.HasMajorRecords(loqui, includeBaseClass: true) == Case.No) return null;
                    return new Members(accessor, Target(loqui), loqui.TypeName(getter: true), Single: true, Threaded: false);
                }
                case ContainerType { SubTypeGeneration: LoquiType item } container:
                {
                    if (await MajorRecordModule.HasMajorRecords(item, includeBaseClass: true) == Case.No) return null;
                    return new Members($"{accessor}{(container.Nullable ? ".EmptyIfNull()" : null)}", Target(item), item.TypeName(getter: true), Single: false, Threaded: IsThreaded(container));
                }
                case DictType { Mode: DictMode.KeyedValue, ValueTypeGen: LoquiType value }:
                {
                    if (await MajorRecordModule.HasMajorRecords(value, includeBaseClass: true) == Case.No) return null;
                    return new Members($"{accessor}.Items", Target(value), value.TypeName(getter: true), Single: false, Threaded: false);
                }
                default:
                    return null;
            }
        }

        private static bool IsThreaded(TypeGeneration field) =>
            field.CustomData.TryGetValue(PluginListBinaryTranslationGeneration.ThreadKey, out var threaded) && threaded is true;

        private static ObjectGeneration? Target(LoquiType loqui) => loqui.RefType switch
        {
            // A link interface's records have nothing nested in them, as EnumerateMajorRecords assumes too.
            LoquiType.LoquiRefType.Interface => null,
            LoquiType.LoquiRefType.Direct => loqui.TargetObjectGeneration,
            _ => throw new NotSupportedException($"{loqui.ObjectGen.Name}.{loqui.Name}: a generic field holding major records, outside a group."),
        };

        /// <summary>Whether an object's fields (its own, or those outside its split fields) hold major records.</summary>
        private async Task<bool> HoldsRecords(ObjectGeneration obj, bool unsplitOnly = false, bool includeBaseClass = true)
        {
            foreach (var field in obj.IterateFields(includeBaseClass: includeBaseClass))
            {
                if (await MembersOf("_", field) is not { } members) continue;
                if (unsplitOnly && await Splits(members)) continue;
                return true;
            }
            return false;
        }

        /// <summary>Whether any subclass of an object declares fields that hold major records.</summary>
        private async Task<bool> AnyDescendantHoldsRecords(ObjectGeneration obj) => (await RecordHoldingDescendants(obj)).Count > 0;

        /// <summary>Subclasses declaring fields that hold major records, the most derived first.</summary>
        private async Task<List<ObjectGeneration>> RecordHoldingDescendants(ObjectGeneration obj)
        {
            var found = new List<(ObjectGeneration Obj, int Depth)>();
            async Task Walk(ObjectGeneration o, int depth)
            {
                foreach (var inheriting in await o.InheritingObjects())
                {
                    if (await HoldsRecords(inheriting, includeBaseClass: false)) found.Add((inheriting, depth));
                    await Walk(inheriting, depth + 1);
                }
            }
            await Walk(obj, 1);
            return found.OrderByDescending(f => f.Depth).Select(f => f.Obj).Distinct().ToList();
        }

        /// <summary>Whether an object, or any subclass, holds major records.</summary>
        private async Task<bool> HoldsRecordsInTree(ObjectGeneration obj) =>
            await HoldsRecords(obj) || await AnyDescendantHoldsRecords(obj);

        /// <summary>Emits one item: a major record with its parent, then whatever is nested in it.</summary>
        private async Task ItemLines(StructuredStringBuilder sb, string item, ObjectGeneration? target, string parent)
        {
            if (target == null || await target.IsMajorRecord())
            {
                sb.AppendLine($"yield return new {Entry}({item}, {parent});");
                if (target != null && await HoldsRecordsInTree(target))
                {
                    Yield(sb, $"{Contents(target)}({item}, {item})");
                }
            }
            else if (await HoldsRecordsInTree(target))
            {
                Yield(sb, $"{Contents(target)}({item}, {parent})");
            }
        }

        private void Yield(StructuredStringBuilder sb, string enumerable)
        {
            var nested = Variable();
            sb.AppendLine($"foreach (var {nested} in {enumerable})");
            using (sb.CurlyBrace())
            {
                sb.AppendLine($"yield return {nested};");
            }
        }

        /// <summary>Emits the records a field holds, and whatever is nested in them.</summary>
        private async Task FieldLines(StructuredStringBuilder sb, string owner, TypeGeneration field, string parent, bool unsplitOnly)
        {
            if (await MembersOf($"{owner}.{field.Name}", field) is not { } members) return;
            if (unsplitOnly && await Splits(members)) return;
            var item = Variable();
            sb.AppendLine(members.Single ? $"if ({members.Accessor} is {{}} {item})" : $"foreach (var {item} in {members.Accessor})");
            using (sb.CurlyBrace())
            {
                await ItemLines(sb, item, members.Target, parent);
            }
        }

        // Helpers, each generated once per mod, named after the object they walk.

        private static string Key(string typeName) => Regex.Replace(typeName, "[^A-Za-z0-9]", "");

        /// <summary>A batch of one item: the item and whatever is nested in it.</summary>
        private async Task<string> ItemBatch(Members members)
        {
            if (members.Target != null && !await members.Target.IsMajorRecord()) return Contents(members.Target);
            return Helper($"Batch{Key(members.TypeName)}Item", async sb =>
            {
                using (var args = sb.Function($"private static {Batch} Batch{Key(members.TypeName)}Item"))
                {
                    args.Add($"{members.TypeName} item");
                    args.Add($"{Parent} parent");
                }
                using (sb.CurlyBrace())
                {
                    await ItemLines(sb, "item", members.Target, "parent");
                }
            });
        }

        /// <summary>A batch of a list of items, and whatever is nested in them.</summary>
        private async Task<string> ListBatch(Members members)
        {
            // Major records with nothing nested in them need no walk of their own.
            if (members.Target == null || (await members.Target.IsMajorRecord() && !await HoldsRecordsInTree(members.Target)))
            {
                return $"{nameof(MajorRecordWithParent)}.{nameof(MajorRecordWithParent.All)}";
            }
            return Helper($"Batch{Key(members.TypeName)}List", async sb =>
            {
                using (var args = sb.Function($"private static {Batch} Batch{Key(members.TypeName)}List"))
                {
                    args.Add($"IEnumerable<{members.TypeName}> items");
                    args.Add($"{Parent} parent");
                }
                using (sb.CurlyBrace())
                {
                    sb.AppendLine("foreach (var item in items)");
                    using (sb.CurlyBrace())
                    {
                        await ItemLines(sb, "item", members.Target, "parent");
                    }
                }
            });
        }

        /// <summary>What is nested in an object (in a subclass's fields too), each with the given parent.</summary>
        private string Contents(ObjectGeneration obj) => Helper($"Batch{obj.Name}Contents", async sb =>
        {
            using (var args = sb.Function($"private static {Batch} Batch{obj.Name}Contents"))
            {
                args.Add($"{obj.Interface(getter: true)} obj");
                args.Add($"{Parent} parent");
            }
            using (sb.CurlyBrace())
            {
                var descendants = await RecordHoldingDescendants(obj);
                if (descendants.Count > 0)
                {
                    sb.AppendLine("switch (obj)");
                    using (sb.CurlyBrace())
                    {
                        foreach (var descendant in descendants)
                        {
                            sb.AppendLine($"case {descendant.Interface(getter: true)} descendant{descendant.Name}:");
                            using (sb.IncreaseDepth())
                            {
                                Yield(sb, $"{Contents(descendant)}(descendant{descendant.Name}, parent)");
                                sb.AppendLine("yield break;");
                            }
                        }
                    }
                }
                var count = sb.Count;
                foreach (var field in obj.IterateFields(includeBaseClass: true))
                {
                    await FieldLines(sb, "obj", field, "parent", unsplitOnly: false);
                }
                if (sb.Count == count) sb.AppendLine("yield break;");
            }
        });

        /// <summary>
        /// A batch of an item that splits, without its split fields: a major record with what is nested in it outside
        /// them, or a block's records outside them.
        /// </summary>
        private string Unsplit(ObjectGeneration obj) => Helper($"Batch{obj.Name}Unsplit", async sb =>
        {
            var isMajor = await obj.IsMajorRecord();
            using (var args = sb.Function($"private static {Batch} Batch{obj.Name}Unsplit"))
            {
                args.Add($"{obj.Interface(getter: true)} obj");
                args.Add($"{Parent} parent");
            }
            using (sb.CurlyBrace())
            {
                var count = sb.Count;
                if (isMajor) sb.AppendLine($"yield return new {Entry}(obj, parent);");
                foreach (var field in obj.IterateFields(includeBaseClass: true))
                {
                    await FieldLines(sb, "obj", field, isMajor ? "obj" : "parent", unsplitOnly: true);
                }
                if (sb.Count == count) sb.AppendLine("yield break;");
            }
        });

        private string Helper(string name, Func<StructuredStringBuilder, Task> generate)
        {
            if (_helpers.TryAdd(name, generate)) _pending.Enqueue(name);
            return name;
        }

        /// <summary>Emits every helper the batches used, and the helpers those use in turn.</summary>
        public async Task GenerateHelpers(StructuredStringBuilder sb)
        {
            var generated = new List<(string Name, StructuredStringBuilder Code)>();
            while (_pending.TryDequeue(out var name))
            {
                var code = new StructuredStringBuilder();
                await _helpers[name](code);
                generated.Add((name, code));
            }
            foreach (var (_, code) in generated.OrderBy(g => g.Name, StringComparer.Ordinal))
            {
                sb.AppendLines(code);
                sb.AppendLine();
            }
        }
    }
}