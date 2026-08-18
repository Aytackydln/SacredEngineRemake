using System.Numerics;
using Sacred.Granny.Abstractions;
using Sacred.Granny.Animation;
using Sacred.Granny.Assets;
using Sacred.Granny.Loading;

namespace Sacred.Assets.Paks.Models;

/// <summary>
/// Serializes access to a stateful GRN backend and prevents a live backend from being
/// disposed while a model load is still using it.
/// </summary>
internal sealed class SwappableGrnAssetLoader : IGrnAssetLoader
{
    private readonly object _sync = new();
    private IGrnAssetLoader _current;
    private bool _disposed;

    public SwappableGrnAssetLoader(IGrnAssetLoader initial) =>
        _current = initial ?? throw new ArgumentNullException(nameof(initial));

    public GrnBackendKind Kind
    {
        get
        {
            lock (_sync)
                return _current.Kind;
        }
    }

    public string DisplayName
    {
        get
        {
            lock (_sync)
                return _current.DisplayName;
        }
    }

    public GrnAsset LoadFromBytes(
        string name,
        byte[] bytes,
        GrnMeshExtractionMode meshExtractionMode = GrnMeshExtractionMode.PrimarySlice,
        Vector3? modelScale = null)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _current.LoadFromBytes(name, bytes, meshExtractionMode, modelScale);
        }
    }

    public GrnAsset LoadCharacterFromBytes(
        string name,
        byte[] baseBytes,
        IReadOnlyList<GrnCharacterAttachment> attachments,
        byte[]? defaultAnimationBytes = null,
        string? defaultAnimationName = null,
        Vector3? baseModelScale = null)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _current.LoadCharacterFromBytes(
                name,
                baseBytes,
                attachments,
                defaultAnimationBytes,
                defaultAnimationName,
                baseModelScale);
        }
    }

    public GrnAnimationClip? TryExtractAnimation(
        byte[] bytes,
        string? name = null,
        Vector3? modelScale = null)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _current.TryExtractAnimation(bytes, name, modelScale);
        }
    }

    public void Replace(IGrnAssetLoader replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (ReferenceEquals(_current, replacement))
                return;

            var previous = _current;
            _current = replacement;
            DisposeOwned(previous);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            var previous = _current;
            _current = ManagedGrnAssetLoader.Instance;
            DisposeOwned(previous);
        }
    }

    private static void DisposeOwned(IGrnAssetLoader loader)
    {
        if (!ReferenceEquals(loader, ManagedGrnAssetLoader.Instance))
            loader.Dispose();
    }
}
