using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Three-byte CreateObj facing operand. The payload is an authored angle in degrees.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptFacingArgumentLayout
{
    public const int SerializedSize = 3;
    [FieldOffset(0)] public readonly SacredScriptArgumentKind Kind;
    [FieldOffset(1)] public readonly ushort FacingDegrees;
}
