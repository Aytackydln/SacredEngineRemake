using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Tag 0x02 and its type identifier. Native decoder: 0x473A27.
/// This is the stored ID from the executable's symbolic type catalogue, not its row index.
/// Sacred.Particles uses it directly as the embedded particle catalogue key.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptTypeArgumentLayout
{
    public const int SerializedSize = 5;
    [FieldOffset(0)] public readonly SacredScriptArgumentKind Kind;
    [FieldOffset(1)] public readonly uint TypeId;
}
