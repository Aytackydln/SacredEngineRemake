namespace Sacred.Core.Binary;

/// <summary>
/// Describes a fixed-width byte range that stores text in a binary game-file layout.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class BinaryStringAttribute : Attribute
{
    /// <summary>
    /// Initializes metadata for a serialized string field.
    /// </summary>
    /// <param name="name">Human-readable field name used by format documentation.</param>
    /// <param name="byteLength">Number of bytes reserved by the serialized field.</param>
    /// <param name="encoding">Encoding name, such as ASCII or ISO-8859-1.</param>
    public BinaryStringAttribute(string name, int byteLength, string encoding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(encoding);

        Name = name;
        ByteLength = byteLength;
        Encoding = encoding;
    }

    /// <summary>Human-readable field name used by format documentation.</summary>
    public string Name { get; }

    /// <summary>Number of bytes reserved by the serialized field.</summary>
    public int ByteLength { get; }

    /// <summary>Name of the character encoding used by the serialized bytes.</summary>
    public string Encoding { get; }

    /// <summary>Whether the first zero byte terminates the decoded value.</summary>
    public bool NullTerminated { get; init; } = true;
}
