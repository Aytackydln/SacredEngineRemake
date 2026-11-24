namespace Sacred.Core.Executable.Layouts;

public static class SacredExecutableCrtBufferLayout
{
    public static ReadOnlySpan<byte> Signature => [0x59, 0x8b, 0x4c, 0x24, 4, 0x85, 0xc0, 0x89, 0x41, 8, 0x74];
    public const int SignatureOffset = 0x10;
    public const int AllocationOperand = 7;
    public const int BufferSizeOperand = 0x23;
    public const int FallbackSizeOperand = 0x36;
}
