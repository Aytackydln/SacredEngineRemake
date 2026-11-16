namespace Sacred.Core.Runtime.Layouts;

/// <summary>Original embedded CRT memory ABI; not a game-file or actor layout.</summary>
public static class SacredCrtFileLayout
{
    public const int DescriptorStride = 0x24;
    public const int HandleOffset = 0;
    public const int DescriptorFlagsOffset = 4;
    public const int SavedCharacterOffset = 5;
    public const int LockInitializedOffset = 8;
    public const byte Open = 1;
    public const byte Eof = 2;
    public const byte LastReadStartsLf = 4;
    public const byte Pipe = 8;
    public const byte Append = 0x20;
    public const byte Device = 0x40;
    public const byte Text = 0x80;
    public const int FilePointerOffset = 0;
    public const int RemainingCountOffset = 4;
    public const int BufferBaseOffset = 8;
    public const int FileFlagsOffset = 0xc;
    public const int DescriptorIndexOffset = 0x10;
    public const int TinyBufferOffset = 0x14;
    public const int BufferSizeOffset = 0x18;
    public const uint CallerOwnedReadBufferFlags = 0x101;
    public const uint CallerOwnedWriteBufferFlags = 0x102;
    public const int WriteStagingThreshold = 0x400;
}
