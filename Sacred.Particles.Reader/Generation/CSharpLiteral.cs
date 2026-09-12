using System.Globalization;
using System.Text;

namespace Sacred.Particles.Reader.Generation;

internal static class CSharpLiteral
{
    public static string String(string? value)
    {
        if (value == null) return "null";
        var text = new StringBuilder("\"");
        foreach (var character in value)
        {
            if (character is '"' or '\\') text.Append('\\').Append(character);
            else if (char.IsControl(character)) text.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            else text.Append(character);
        }
        return text.Append('"').ToString();
    }

    public static string UInt32(uint value) => $"0x{value:X}u";
    public static string Int32(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "null";
}
