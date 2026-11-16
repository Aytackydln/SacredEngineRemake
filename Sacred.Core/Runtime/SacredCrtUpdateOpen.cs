namespace Sacred.Core.Runtime;

/// <summary>Gold 0x857EEF..0x857F60 text update preparation, including the
/// reached shrinking branch of _chsize. Caller owns failure close/cleanup.</summary>
public static class SacredCrtUpdateOpen
{
    public static bool Prepare(SacredCrtDescriptor descriptor, SacredCrtUpdateServices services)
    {
        var tail = Seek(-1, 2);
        if (tail == uint.MaxValue) return services.GetLastError() == 131;
        var state = new SacredCrtInputState(descriptor.Flags) { Lookahead = descriptor.Lookahead };
        var value = new byte[1];
        var count = SacredCrtInput.Read(state, new(services.Read, (offset, origin) => Seek(offset, origin) != uint.MaxValue), value);
        descriptor.Flags = state.Flags; descriptor.Lookahead = state.Lookahead;
        if (count == 0 && value[0] == 0x1a && !Shrink(tail)) return false;
        return Seek(0, 0) != uint.MaxValue;

        uint Seek(int offset, int origin)
        {
            var result = services.Seek(offset, origin);
            // _lseek clears sticky text EOF after a successful platform seek.
            if (result != uint.MaxValue || services.GetLastError() == 0) descriptor.Flags &= 0xfd;
            return result;
        }
        bool Shrink(uint length)
        {
            var saved = Seek(0, 1);
            if (saved == uint.MaxValue) return false;
            var end = Seek(0, 2);
            if (end == uint.MaxValue) return false;
            // The tail probe reaches only shrink/equal; general _chsize growth
            // has separate binary-mode/write semantics and is not this caller.
            if (length > end) throw new InvalidOperationException("Update tail moved beyond the established file end.");
            var success = true;
            if (length < end)
            {
                _ = Seek(unchecked((int)length), 0);
                success = services.Truncate();
            }
            _ = Seek(unchecked((int)saved), 0); // original ignores restoration failure
            return success;
        }
    }
}
