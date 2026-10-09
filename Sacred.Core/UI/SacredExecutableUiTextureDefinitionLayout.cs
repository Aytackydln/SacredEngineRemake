using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.UI;

/// <summary>
/// Native sUITextureDef in the inspected Windows Demo, Gold and pureHD images.
/// The executable stores authored texel coordinates; its loader resolves the texture
/// handle, computes dimensions/anchors and normalizes UVs in place.
/// See docs/research/ui-components/README.md for identities and verification.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x54)]
public readonly struct SacredExecutableUiTextureDefinitionLayout
{
    [FieldOffset(0x00)] public readonly uint Definition;
    [FieldOffset(0x04)] public readonly SacredExecutableUiAnchorKind Type;
    [FieldOffset(0x08), BinaryString("Texture name", 32, "ASCII")]
    public readonly SacredExecutableUiTextureName TextureName;
    [FieldOffset(0x28)] public readonly uint TextureHandle;
    [FieldOffset(0x2C)] public readonly float U0;
    [FieldOffset(0x30)] public readonly float V0;
    [FieldOffset(0x34)] public readonly float U1;
    [FieldOffset(0x38)] public readonly float V1;
    [FieldOffset(0x3C)] public readonly int AnchorX1;
    [FieldOffset(0x40)] public readonly int AnchorY1;
    [FieldOffset(0x44)] public readonly int AnchorX2;
    [FieldOffset(0x48)] public readonly int AnchorY2;
    [FieldOffset(0x4C)] public readonly int Width;
    [FieldOffset(0x50)] public readonly int Height;
}
