namespace Sacred.GameplayHost.Runtime;

/// <summary>Logical FILE storage; unreset tiny-buffer/size words survive reuse.</summary>
public sealed class SacredCrtFileSlot
{
    public uint Flags { get; set; }
    public int Descriptor { get; set; } = -1;
    public uint TinyBuffer { get; set; }
    public uint BufferSize { get; set; }
    public SacredCrtBufferStorage Storage { get; set; }
    public void Reset() { Flags = 0; Descriptor = -1; Storage = SacredCrtBufferStorage.None; }
    public uint[] Words() => [0, 0, 0, Flags, unchecked((uint)Descriptor), TinyBuffer, BufferSize, 0];
}
