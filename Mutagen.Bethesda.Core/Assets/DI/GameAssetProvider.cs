using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Assets.DI;

public class GameAssetProvider : IAssetProvider
{
    private readonly DataDirectoryAssetProvider _dataAssetProvider;
    private readonly ArchiveAssetProvider _archiveAssetProvider;

    public GameAssetProvider(
        DataDirectoryAssetProvider dataAssetProvider,
        ArchiveAssetProvider archiveAssetProvider) {
        _dataAssetProvider = dataAssetProvider;
        _archiveAssetProvider = archiveAssetProvider;
    }

    public bool Exists(DataRelativePath assetPath) {
        Mutagen.Bethesda.Plugins.ReadObservation.Report("unsupported", Mutagen.Bethesda.Plugins.ModKey.Null, "assets");
        return _dataAssetProvider.Exists(assetPath) || _archiveAssetProvider.Exists(assetPath);
    }

    public bool TryGetStream(DataRelativePath assetPath, [MaybeNullWhen(false)] out Stream stream) {
        Mutagen.Bethesda.Plugins.ReadObservation.Report("unsupported", Mutagen.Bethesda.Plugins.ModKey.Null, "assets");
        if (_dataAssetProvider.TryGetStream(assetPath, out stream)) return true;
        if (_archiveAssetProvider.TryGetStream(assetPath, out stream)) return true;

        return false;
    }

    public bool TryGetSize(DataRelativePath assetPath, out uint size) {
        Mutagen.Bethesda.Plugins.ReadObservation.Report("unsupported", Mutagen.Bethesda.Plugins.ModKey.Null, "assets");
        if (_dataAssetProvider.TryGetSize(assetPath, out size)) return true;
        if (_archiveAssetProvider.TryGetSize(assetPath, out size)) return true;

        return false;
    }
}
