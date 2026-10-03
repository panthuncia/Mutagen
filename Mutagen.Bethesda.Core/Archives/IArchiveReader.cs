using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Archives;

/// <summary>
/// A readonly interface for retrieving data from a Bethesda Archive
/// </summary>
public interface IArchiveReader
{
    /// <summary>
    /// Attempts to locate and retrieve a folder from the archive
    /// </summary>
    /// <param name="path">Folder path to look for</param>
    /// <param name="folder">Retrieved folder object, if successful</param>
    /// <returns>True if folder with the given path was located in the archive</returns>
    bool TryGetFolder(string path, [MaybeNullWhen(false)] out IArchiveFolder folder);
    IEnumerable<IArchiveFile> Files { get; }

    /// <summary>
    /// Every file's path, as <see cref="IArchiveFile.Path"/> gives it, in the order of <see cref="Files"/>: for listing an
    /// archive, which a reader may do without making an object for each file.
    /// </summary>
    IEnumerable<string> FilePaths => Files.Select(f => f.Path);
}