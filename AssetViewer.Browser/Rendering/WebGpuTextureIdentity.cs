using System.Runtime.CompilerServices;
using AssetViewer.ItemViewer;

namespace AssetViewer.Browser.Rendering;

/// <summary>Animation variants share pixel storage and one device texture. Weak keys don't retain decoded pixels.</summary>
internal sealed class WebGpuTextureIdentity
{
    private static readonly ConditionalWeakTable<byte[], WebGpuTextureIdentity> Identities = new();
    private static int _next;
    internal int Key { get; }
    internal bool Translucent { get; }
    private WebGpuTextureIdentity(byte[] pixels)
    {
        Key = Interlocked.Increment(ref _next);
        Translucent = ModelPreviewMaterials.HasTranslucentPixels(pixels);
    }
    internal static WebGpuTextureIdentity For(byte[] pixels) => Identities.GetValue(pixels, static data => new(data));
}
