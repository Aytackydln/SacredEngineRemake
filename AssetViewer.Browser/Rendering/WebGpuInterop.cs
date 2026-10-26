using System.Runtime.InteropServices.JavaScript;
using AssetViewer.Browser.Files;

namespace AssetViewer.Browser.Rendering;

internal static partial class WebGpuInterop
{
    [JSImport("registerShader", "webgpu")]
    internal static partial void RegisterShader(string name, string source);
    [JSImport("ensureAvailable", "webgpu")]
    internal static partial Task EnsureAvailableAsync();
    [JSImport("createHost", "webgpu")]
    internal static partial JSObject CreateHost(
        [JSMarshalAs<JSType.Function<JSType.Number>>] Action<double> drag,
        [JSMarshalAs<JSType.Function<JSType.Number>>] Action<double> wheel);
    [JSImport("initialize", "webgpu")]
    internal static partial Task InitializeAsync(JSObject host);
    [JSImport("startFrames", "webgpu")]
    internal static partial void StartFrames(JSObject host,
        [JSMarshalAs<JSType.Function<JSType.Number>>] Action<double> render);
    [JSImport("stopFrames", "webgpu")]
    internal static partial void StopFrames(JSObject host);
    [JSImport("setSkinning", "webgpu")]
    internal static partial void SetSkinning(JSObject host,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> vertices,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> influences,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> projection, int bones, int vertexCount);
    [JSImport("setSkinningPose", "webgpu")]
    internal static partial void SetSkinningPose(JSObject host,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> regular,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> rigid);
    [JSImport("readSkinningVertices", "webgpu")]
    private static partial Task<JSObject> ReadSkinningVerticesObjectAsync(JSObject host);
    internal static async Task<byte[]> ReadSkinningVerticesAsync(JSObject host)
    {
        using var source = await ReadSkinningVerticesObjectAsync(host);
        return BrowserInterop.ToBytes(source);
    }
    [JSImport("setMesh", "webgpu")]
    internal static partial void SetMesh(JSObject host, string key,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> vertices,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> indices, bool updateIndices);
    [JSImport("setTexture", "webgpu")]
    internal static partial void SetTexture(JSObject host, string name, int width, int height,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> rgba, int key);
    [JSImport("clearResources", "webgpu")]
    internal static partial void ClearResources(JSObject host);
    [JSImport("beginFrame", "webgpu")]
    internal static partial void BeginFrame(JSObject host, int width, int height,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> scene, bool transparent);
    [JSImport("draw", "webgpu")]
    internal static partial void Draw(JSObject host, string mesh, string pipeline, string texture, string overlay,
        string glow, int start, int count, [JSMarshalAs<JSType.MemoryView>] Span<byte> constants);
    [JSImport("endFrame", "webgpu")]
    internal static partial void EndFrame(JSObject host);
    [JSImport("screenshot", "webgpu")]
    internal static partial void Screenshot(JSObject host, string name);
    [JSImport("readPixels", "webgpu")]
    private static partial Task<JSObject> ReadPixelsObjectAsync(JSObject host);
    internal static async Task<byte[]> ReadPixelsAsync(JSObject host)
    {
        using var pixels = await ReadPixelsObjectAsync(host);
        return BrowserInterop.ToBytes(pixels);
    }
    [JSImport("destroy", "webgpu")]
    internal static partial void Destroy(JSObject host);
}
