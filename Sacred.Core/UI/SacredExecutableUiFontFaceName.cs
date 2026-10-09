using System.Runtime.CompilerServices;

namespace Sacred.Core.UI;

/// <summary>Native font definition's 64-byte, NUL-terminated face name.</summary>
[InlineArray(64)]
public struct SacredExecutableUiFontFaceName
{
    private byte _first;
}
