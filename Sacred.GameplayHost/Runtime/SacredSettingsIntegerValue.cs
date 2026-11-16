using System.Globalization;
using System.Text;

namespace Sacred.GameplayHost.Runtime;

public static class SacredSettingsIntegerValue
{
    /// <summary>Compared 0x662DF0 sprintf signed-32-bit decimal path. Unsupported
    /// executable format data is explicit rather than replaced by a default.</summary>
    public static byte[] Format(ReadOnlySpan<byte> format, uint value)
    {
        if (!format.SequenceEqual("%d"u8)) throw new NotSupportedException("Original settings integer format requires another comparison.");
        return Encoding.ASCII.GetBytes(unchecked((int)value).ToString(CultureInfo.InvariantCulture));
    }
}
