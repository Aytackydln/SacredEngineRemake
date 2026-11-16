namespace Sacred.GameplayHost.Runtime;

public static class SacredCrtFileOpen
{
    public static SacredCrtFileSlot? Open(ReadOnlyMemory<byte> path, ReadOnlyMemory<byte> mode,
        SacredCrtFilePool files, SacredCrtDescriptorPool descriptors, SacredCrtOpenServices services,
        uint defaultCommitFlags, uint defaultFileMode, uint umask, uint share = 0x40)
    {
        var file = files.Acquire(services.Allocate);
        if (file is null) return null;
        var parsed = SacredCrtOpenMode.Parse(mode.Span, defaultCommitFlags);
        if (parsed is not { } flags) return null;
        var request = SacredCrtOpenPolicy.Create(path, flags.OpenFlags, share, 0x1a4, umask);
        if (request is null) return null;
        var allocated = descriptors.Acquire(services.Allocate);
        if (allocated is not { } descriptor) return null;
        var handle = services.Create(request);
        if (handle == uint.MaxValue) { _ = services.GetLastError(); return null; }
        var type = services.GetFileType(handle);
        if (type == 0) { _ = services.Close(handle); _ = services.GetLastError(); return null; }
        descriptor.Slot.Handle = handle;
        var finalFlags = SacredCrtOpenPolicy.DescriptorFlags(flags.OpenFlags, defaultFileMode, type);
        descriptor.Slot.Flags = (byte)(finalFlags & ~0x20);
        if ((descriptor.Slot.Flags & 0xc8) == 0x80 && (flags.OpenFlags & 2) != 0 && !services.PrepareUpdate(descriptor.Slot))
        {
            _ = services.Close(handle); descriptor.Slot.Handle = uint.MaxValue; descriptor.Slot.Flags = 0; return null;
        }
        descriptor.Slot.Flags |= (byte)(finalFlags & 0x20);
        file.Flags = flags.FileFlags; file.Descriptor = descriptor.Index;
        return file;
    }
}
