namespace Sacred.Core.Pak.Texture;

/// <summary>
/// Storage format byte repeated in each Texture.pak descriptor and texture header.
/// This byte selects decompression/pixel decoding only; it is not a shader id.
/// </summary>
public enum SacredTextureStorageFormat : byte
{
    Argb4444 = 0,
    RleArgb4444 = 3,
    ZlibArgb4444 = 4,
    Bgra8888 = 6
}
