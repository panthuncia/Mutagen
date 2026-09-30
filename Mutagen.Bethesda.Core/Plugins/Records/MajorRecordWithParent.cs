namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// A major record, and the major record it is nested in: a cell for its placed references, a worldspace for its
/// cells, a topic for its responses. <see cref="Parent"/> is null for a record directly in a top-level group, or in
/// the interior cell blocks.
/// </summary>
public readonly record struct MajorRecordWithParent(IMajorRecordGetter Record, IMajorRecordGetter? Parent)
{
    /// <summary>Records with nothing nested in them, all with the same parent.</summary>
    public static IEnumerable<MajorRecordWithParent> All(IEnumerable<IMajorRecordGetter> records, IMajorRecordGetter? parent)
    {
        foreach (var record in records)
        {
            yield return new MajorRecordWithParent(record, parent);
        }
    }
}