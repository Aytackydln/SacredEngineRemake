using System.Runtime.InteropServices;

namespace Sacred.Assets.Paks.Models;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct ModelPakDescriptor(uint EntryId, uint Offset, uint PayloadSize)
{
    public const int SerializedSize = 0x0C;
}
