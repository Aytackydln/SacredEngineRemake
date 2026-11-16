namespace Sacred.GameplayHost.Runtime;

/// <summary>Gold 0x84F5E4 _read_nolock at ReadFile/SetFilePointer boundaries.
/// No Unicode decoding or generic newline replacement is performed.</summary>
public static class SacredCrtInput
{
    public static int Read(SacredCrtInputState state, SacredCrtReadServices services, Memory<byte> destination)
    {
        if (destination.IsEmpty || (state.Flags & 2) != 0) return 0;
        var prefix = 0;
        if ((state.Flags & 0x48) != 0 && state.Lookahead != 10)
        {
            destination.Span[0] = state.Lookahead; prefix = 1; state.Lookahead = 10;
        }
        var result = services.Read(destination[prefix..]);
        if (!result.Success) return result.Error == 109 ? 0 : -1;
        if (result.Count < 0 || result.Count > destination.Length - prefix) throw new InvalidOperationException("Supplied ReadFile count exceeds its destination.");
        var count = result.Count + prefix;
        if ((state.Flags & 0x80) == 0) return count;
        state.Flags = (byte)(result.Count != 0 && destination.Span[0] == 10 ? state.Flags | 4 : state.Flags & ~4);
        var written = 0; var index = 0;
        while (index < count)
        {
            var value = destination.Span[index++];
            if (value == 0x1a)
            {
                if ((state.Flags & 0x40) == 0) state.Flags |= 2;
                break;
            }
            if (value != 13) { destination.Span[written++] = value; continue; }
            if (index < count)
            {
                if (destination.Span[index] == 10) { index++; destination.Span[written++] = 10; }
                else destination.Span[written++] = 13;
                continue;
            }
            var peek = new byte[1]; var next = services.Read(peek);
            if (!next.Success || next.Count == 0) { destination.Span[written++] = 13; continue; }
            if (next.Count != 1) throw new InvalidOperationException("Supplied lookahead exceeds one byte.");
            if ((state.Flags & 0x48) != 0)
            {
                if (peek[0] == 10) destination.Span[written++] = 10;
                else { destination.Span[written++] = 13; state.Lookahead = peek[0]; }
            }
            else if (written == 0 && peek[0] == 10) destination.Span[written++] = 10;
            else
            {
                if (services.Seek(-1, 1)) state.Flags &= 0xfd;
                if (peek[0] != 10) destination.Span[written++] = 13;
            }
        }
        return written;
    }
}
