using System.Collections.Concurrent;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Mapping;

namespace Mutagen.Bethesda.Plugins.Cache.Internals.Implementations.Internal;

/// <summary>
/// Finds a mod's record by FormKey through its top-level groups, which are keyed by FormKey, rather than by enumerating
/// the mod. It keeps no record of its own, so it stays right as the mod changes, and serves the mutable link caches.
/// A type is looked up this way when every record type it covers (itself, or each type behind an interface) lives in
/// a top-level group of its own, as the mod's generated <c>TryGetTopLevelGroup</c> says; its records then appear nowhere
/// else in the mod. Nested records (cells, placed references, a topic's responses) and a query for any record at all are
/// left to the caller's enumeration.
/// </summary>
internal sealed class TopLevelRecordLookup
{
    private readonly IModGetter _mod;
    private readonly GameCategory _category;
    private readonly ConcurrentDictionary<Type, IGroupGetter[]?> _groups = new();

    public TopLevelRecordLookup(IModGetter mod)
    {
        _mod = mod;
        _category = mod.GameRelease.ToCategory();
    }

    /// <summary>
    /// Whether the mod holds a record of <paramref name="type"/> with this FormKey, and that record; or null when the
    /// type's records are not all in top-level groups, and the mod has to be enumerated to know.
    /// </summary>
    public bool? TryFind(FormKey formKey, Type type, out IMajorRecordGetter? record)
    {
        record = null;
        if (_groups.GetOrAdd(type, Groups) is not { } groups) return null;
        foreach (var group in groups)
        {
            if (!group.ContainsKey(formKey)) continue;
            var found = group[formKey];
            // A FormKey is one record in a mod: one of another type (a float global, asked for an int one) means none.
            if (!type.IsInstanceOfType(found)) return false;
            record = found;
            return true;
        }
        return false;
    }

    private IGroupGetter[]? Groups(Type type)
    {
        IEnumerable<Type> types = [type];
        if (MetaInterfaceMapping.Instance.TryGetRegistrationsForInterface(_category, type, out var mapping))
        {
            types = mapping.Registrations.Select(r => mapping.Setter ? r.SetterType : r.GetterType);
        }
        var groups = new List<IGroupGetter>();
        foreach (var recordType in types)
        {
            IGroupGetter? group;
            try
            {
                group = _mod.TryGetTopLevelGroup(recordType);
            }
            catch (ArgumentException)
            {
                group = null;
            }
            if (group == null) return null;
            // By reference: groups compare by content, and two empty groups are equal.
            if (!groups.Any(g => ReferenceEquals(g, group))) groups.Add(group);
        }
        return groups.Count == 0 ? null : [.. groups];
    }
}