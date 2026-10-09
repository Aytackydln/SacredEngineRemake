using System.Runtime.CompilerServices;

namespace Sacred.Core.UI;

/// <summary>The original 32-byte, NUL-terminated sUITextureDef texture name.</summary>
[InlineArray(32)]
public struct SacredExecutableUiTextureName
{
    private byte _first;
}
