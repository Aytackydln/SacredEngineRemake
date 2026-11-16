using System.Runtime.InteropServices;
using System.Text;
using Sacred.GameplayHost.Networking;

internal static class SessionImportConversionFixture
{
    public static WorldAnsiConversionServices Create(List<object[]> events, bool fail) => new((value, buffer) =>
    {
        var end = value.Span.IndexOf((byte)0); var bytes = (end < 0 ? value.Span : value.Span[..end]).ToArray();
        events.Add(["api-decode", Hex(bytes), buffer.Length]);
        if (fail) return 0;
        var produced = Encoding.Latin1.GetChars(bytes).Append('\0').ToArray();
        if (!buffer.IsEmpty) produced.AsSpan(0, Math.Min(produced.Length, buffer.Length)).CopyTo(buffer.Span);
        return produced.Length;
    }, (value, buffer) =>
    {
        events.Add(["api-encode", Hex(MemoryMarshal.AsBytes(value.Span)), buffer.Length]);
        if (fail) return 0;
        var produced = Encoding.Latin1.GetBytes(value.ToArray());
        if (!buffer.IsEmpty) produced.AsSpan(0, Math.Min(produced.Length, buffer.Length)).CopyTo(buffer.Span);
        return produced.Length;
    }, count => new char[count], count => new byte[count]);
    private static string Hex(ReadOnlySpan<byte> value) => Convert.ToHexString(value).ToLowerInvariant();
}
