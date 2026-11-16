namespace Sacred.GameplayHost.Runtime;

public static class SacredCrtOpenPolicy
{
    public static SacredCrtCreateRequest? Create(ReadOnlyMemory<byte> path, uint flags, uint share, uint permission, uint umask)
    {
        var access = (flags & 3) switch { 0 => 0x80000000u, 1 => 0x40000000u, 2 => 0xc0000000u, _ => 0u };
        var sharing = share switch { 0x10 => 0u, 0x20 => 1u, 0x30 => 2u, 0x40 => 3u, _ => uint.MaxValue };
        if (access == 0 || sharing == uint.MaxValue) return null;
        var creation = (flags & 0x700) switch { 0 or 0x400 => 3u, 0x100 => 4u, 0x200 or 0x600 => 5u, 0x300 => 2u, 0x500 or 0x700 => 1u, _ => 0u };
        if (creation == 0) return null;
        var attributes = (flags & 0x100) != 0 && ((~umask & permission) & 0x80) == 0 ? 1u : 0x80u;
        if ((flags & 0x40) != 0) { attributes |= 0x4000000; access |= 0x10000; }
        if ((flags & 0x1000) != 0) attributes |= 0x100;
        if ((flags & 0x20) != 0) attributes |= 0x8000000;
        else if ((flags & 0x10) != 0) attributes |= 0x10000000;
        var nul = path.Span.IndexOf((byte)0);
        return new((nul < 0 ? path : path[..nul]).ToArray(), access, sharing, (flags & 0x80) == 0, creation, attributes);
    }
    public static byte DescriptorFlags(uint openFlags, uint defaultFileMode, uint fileType)
    {
        var flags = (byte)(1 | ((openFlags & 0x80) != 0 ? 0x10 : 0));
        if ((openFlags & 0x8000) == 0 && ((openFlags & 0x4000) != 0 || defaultFileMode != 0x8000)) flags |= 0x80;
        if (fileType == 2) flags |= 0x40;
        else if (fileType == 3) flags |= 8;
        if ((flags & 0x48) == 0 && (openFlags & 8) != 0) flags |= 0x20;
        return flags;
    }
}
