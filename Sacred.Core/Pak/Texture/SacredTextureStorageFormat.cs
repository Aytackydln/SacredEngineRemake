namespace Sacred.Core.Pak.Texture;

/// <summary>
/// Native <c>sTextureEntry::type</c> values handled by Sacred's texture loader.
/// The value selects payload decoding and destination pixel depth. It does not
/// select blending, lighting, or a shader.
/// </summary>
public enum SacredTextureStorageFormat : byte
{
    /// <summary>Raw 16-bit pixels copied one scanline at a time.</summary>
    Argb4444 = 0,

    /// <summary>
    /// Legacy raw 16-bit code. The demo and Gold loaders treat it exactly like
    /// value 0; no archive using this value has been found.
    /// </summary>
    Argb4444Variant1 = 1,

    /// <summary>
    /// Legacy raw 16-bit code. The demo and Gold loaders treat it exactly like
    /// value 0; no archive using this value has been found.
    /// </summary>
    Argb4444Variant2 = 2,

    /// <summary>Run-length encoded 16-bit A4R4G4B4 pixels.</summary>
    RleArgb4444 = 3,

    /// <summary>Zlib-compressed 16-bit A4R4G4B4 pixels.</summary>
    ZlibArgb4444 = 4,

    /// <summary>JPEG decoded by the loader and converted to A4R4G4B4.</summary>
    JpegArgb4444 = 5,

    /// <summary>Raw 32-bit pixels copied directly to an A8R8G8B8 surface.</summary>
    Bgra8888 = 6,

    /// <summary>
    /// The loader creates and locks a texture surface but deliberately copies
    /// no archive payload. No archive using this value has been found.
    /// </summary>
    NoPayloadCopy = 7,

    /// <summary>JPEG decoded directly to an A8R8G8B8 surface.</summary>
    JpegBgra8888 = 8
}
