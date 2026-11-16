namespace Sacred.GameplayHost.Runtime;

/// <summary>Original 0x84FAF5 parser. Unknown/repeated suffixes stop parsing
/// while retaining earlier flags; an invalid first byte rejects opening.</summary>
public readonly record struct SacredCrtOpenMode(uint OpenFlags, uint FileFlags)
{
    public static SacredCrtOpenMode? Parse(ReadOnlySpan<byte> mode, uint defaultCommitFlags)
    {
        if (mode.IsEmpty) return null;
        uint flags; var file = defaultCommitFlags;
        switch (mode[0])
        {
            case (byte)'r': flags = 0; file |= 1; break;
            case (byte)'w': flags = 0x301; file |= 2; break;
            case (byte)'a': flags = 0x109; file |= 2; break;
            default: return null;
        }
        var commitSeen = false; var hintSeen = false;
        for (var index = 1; index < mode.Length && mode[index] != 0; index++)
        {
            switch (mode[index])
            {
                case (byte)'+':
                    if ((flags & 2) != 0) return new(flags, file);
                    flags = (flags & ~1u) | 2; file = (file & ~3u) | 0x80; break;
                case (byte)'b': case (byte)'t':
                    if ((flags & 0xc000) != 0) return new(flags, file);
                    flags |= mode[index] == 'b' ? 0x8000u : 0x4000u; break;
                case (byte)'c': case (byte)'n':
                    if (commitSeen) return new(flags, file);
                    commitSeen = true; file = mode[index] == 'c' ? file | 0x4000 : file & ~0x4000u; break;
                case (byte)'S': case (byte)'R':
                    if (hintSeen) return new(flags, file);
                    hintSeen = true; flags |= mode[index] == 'S' ? 0x20u : 0x10u; break;
                case (byte)'D':
                    if ((flags & 0x40) != 0) return new(flags, file);
                    flags |= 0x40; break;
                case (byte)'T':
                    if ((flags & 0x1000) != 0) return new(flags, file);
                    flags |= 0x1000; break;
                default: return new(flags, file);
            }
        }
        return new(flags, file);
    }
}
