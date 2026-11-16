namespace Sacred.GameplayHost.Runtime;

/// <summary>Gold 0x852804 _write_nolock at WriteFile/seek boundaries. Partial
/// text writes retain the original return arithmetic, including negative counts.</summary>
public static class SacredCrtOutput
{
    public static int Write(SacredCrtOutputState state, SacredCrtWriteServices services, ReadOnlyMemory<byte> source)
    {
        if (source.IsEmpty) return 0;
        if ((state.Flags & 0x20) != 0 && services.Seek(0, 2)) state.Flags &= 0xfd;
        var written = 0; var extra = 0; uint error = 0;
        if ((state.Flags & 0x80) == 0)
        {
            var result = services.Write(source);
            if (result.Success) written = result.Count;
            else error = result.Error;
        }
        else
        {
            var consumed = 0;
            while (consumed < source.Length)
            {
                var bytes = new List<byte>();
                do
                {
                    var value = source.Span[consumed++];
                    if (value == 10) { bytes.Add(13); extra++; }
                    bytes.Add(value);
                } while (bytes.Count < 0x400 && consumed < source.Length);
                var result = services.Write(bytes.ToArray());
                if (!result.Success) { error = result.Error; break; }
                if (result.Count < 0 || result.Count > bytes.Count) throw new InvalidOperationException("Supplied WriteFile count exceeds staging bytes.");
                written += result.Count;
                if (result.Count < bytes.Count) break;
            }
        }
        if (written != 0) return written - extra;
        if (error != 0) return -1;
        return (state.Flags & 0x40) != 0 && source.Span[0] == 0x1a ? 0 : -1;
    }
}
