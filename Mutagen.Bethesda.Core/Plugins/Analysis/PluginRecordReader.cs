using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Abstractions;
using Loqui;
using Microsoft.Win32.SafeHandles;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Internals;
using Mutagen.Bethesda.Plugins.Utility;
using Mutagen.Bethesda.Strings;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Analysis;

/// <summary>
/// Reads single major records of a plugin from where they start in the file (a record header's position, as
/// <see cref="RecordLocator"/> finds it), without reading the rest of the plugin. Each record is read as an overlay
/// over its own bytes, so it holds nothing open. A record that holds others (a cell, worldspace or dialog topic) is
/// read without them: only its own fields. Safe to use from several threads.
/// </summary>
public sealed class PluginRecordReader : IDisposable
{
    /// <summary>The header flag of a plugin whose strings are in strings files, in every game that has them.</summary>
    private const int LocalizedFlag = 0x80;

    private static readonly ConcurrentDictionary<Type, Func<OverlayStream, BinaryOverlayFactoryPackage, IMajorRecordGetter>> Factories = new();

    private readonly BinaryOverlayFactoryPackage _package;
    private readonly GameConstants _constants;
    private readonly SafeFileHandle? _handle;
    private readonly Stream? _stream;

    private PluginRecordReader(BinaryOverlayFactoryPackage package, SafeFileHandle? handle, Stream? stream)
    {
        _package = package;
        _constants = package.MetaData.Constants;
        _handle = handle;
        _stream = stream;
    }

    /// <summary>The plugin's FormIDs are read against its masters, and its strings from its strings files, as an overlay of the whole plugin would read them.</summary>
    public static PluginRecordReader FromPath(ModPath path, GameRelease release, BinaryReadParameters? param = null)
    {
        param ??= BinaryReadParameters.Default;
        var meta = ParsingMeta.Factory(param, release, path);
        // As the whole plugin's overlay does: some fields (owners) are parsed by the type of the record they name.
        meta.RecordInfoCache = new RecordTypeInfoCacheReader(() => new MutagenBinaryReadStream(path, meta), path.ModKey, meta.LinkCache);
        var header = ModHeaderFrame.FromPath(path, release, fileSystem: param.FileSystem);
        if (meta.Constants.UsesStrings && (header.Flags & LocalizedFlag) != 0)
        {
            meta.StringsLookup = StringsFolderLookupOverlay.TypicalFactory(release, path.ModKey, Path.GetDirectoryName(path.Path)!, param.StringsParam, fileSystem: param.FileSystem);
        }
        var package = new BinaryOverlayFactoryPackage(meta);
        if (param.FileSystem is null or FileSystem)
        {
            return new PluginRecordReader(package, File.OpenHandle(path.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), null);
        }
        return new PluginRecordReader(package, null, param.FileSystem.FileStream.New(path.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
    }

    /// <summary>The record whose header starts at <paramref name="position"/>, read as <paramref name="getterType"/>.</summary>
    /// <param name="getterType">The record's getter interface, such as <c>INpcGetter</c>.</param>
    public IMajorRecordGetter Read(long position, Type getterType)
    {
        var headerLength = _constants.MajorConstants.HeaderLength;
        Span<byte> header = stackalloc byte[headerLength];
        ReadAt(header, position);
        if (new RecordType(BinaryPrimitives.ReadInt32LittleEndian(header)) == RecordTypes.GRUP)
        {
            throw new ArgumentException($"A group, not a record, starts at {position}.", nameof(position));
        }
        var bytes = new byte[checked(headerLength + (int)BinaryPrimitives.ReadUInt32LittleEndian(header[4..]))];
        ReadAt(bytes, position);
        var factory = Factories.GetOrAdd(getterType, static type =>
            typeof(PluginRecordReader).GetMethod(nameof(Create), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(type)
                .CreateDelegate<Func<OverlayStream, BinaryOverlayFactoryPackage, IMajorRecordGetter>>());
        return factory(new OverlayStream(new MemorySlice<byte>(bytes), _package), _package);
    }

    /// <summary>The record whose header starts at <paramref name="position"/>.</summary>
    public TGetter Read<TGetter>(long position)
        where TGetter : class, IMajorRecordGetter => (TGetter)Read(position, typeof(TGetter));

    private static IMajorRecordGetter Create<TGetter>(OverlayStream stream, BinaryOverlayFactoryPackage package)
        where TGetter : IMajorRecordGetter
    {
        if (LoquiRegistration.GetRegister(typeof(TGetter)) is null)
        {
            throw new ArgumentException($"{typeof(TGetter)} is not a record getter type.");
        }
        return LoquiBinaryOverlayTranslation<TGetter>.Create(stream, package, null);
    }

    private void ReadAt(Span<byte> buffer, long position)
    {
        int read;
        if (_handle is not null)
        {
            read = RandomAccess.Read(_handle, buffer, position);
        }
        else
        {
            lock (_stream!)
            {
                _stream.Position = position;
                read = _stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            }
        }
        if (read != buffer.Length) throw new EndOfStreamException($"The record at {position} runs past the end of the file.");
    }

    public void Dispose()
    {
        _handle?.Dispose();
        _stream?.Dispose();
    }
}
