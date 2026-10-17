using System.Buffers.Binary;
using System.Text;

namespace Sacred.Network.Gameplay;

/// <summary>Lossless 72-byte bank-2 name record. Trigger and script-resource IDs have separate domains.</summary>
public sealed class GoldScriptNameRecord
{
    private readonly byte[] _bytes;
    private GoldScriptNameRecord(byte[] bytes) => _bytes = bytes;
    public string Name => GoldScriptNames.Read(_bytes.AsSpan(0, GoldScriptResourceLayout.NameSize));
    public uint TriggerReference => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(GoldScriptResourceLayout.TriggerReferenceOffset));
    public uint ResourceId => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(GoldScriptResourceLayout.ResourceIdOffset));
    public byte[] ToPayload() => _bytes.ToArray();
    public static GoldScriptNameRecord FromPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != GoldScriptResourceLayout.NameRecordSize) throw new InvalidDataException("Script name record requires 72 bytes.");
        return new(bytes.ToArray());
    }
    internal static GoldScriptNameRecord Create(string name, uint resource) => Create(name, resource, uint.MaxValue);
    internal static GoldScriptNameRecord Create(string name, uint resource, uint trigger)
    {
        var bytes = new byte[GoldScriptResourceLayout.NameRecordSize];
        GoldScriptNames.Encode(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldScriptResourceLayout.TriggerReferenceOffset), trigger);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldScriptResourceLayout.ResourceIdOffset), resource);
        return new(bytes);
    }
    internal GoldScriptNameRecord WithResource(string name, uint resource)
    {
        var bytes = _bytes.ToArray();
        // Native strcpy changes the spelling and terminator, retaining subsequent bytes.
        GoldScriptNames.Encode(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldScriptResourceLayout.ResourceIdOffset), resource);
        return new(bytes);
    }
    internal GoldScriptNameRecord WithTrigger(string name, uint trigger)
    {
        var bytes = _bytes.ToArray();
        GoldScriptNames.Encode(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldScriptResourceLayout.TriggerReferenceOffset), trigger);
        return new(bytes);
    }
}

internal static class GoldScriptNames
{
    public static string Read(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
    }
    public static byte[] Encode(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length >= GoldScriptResourceLayout.NameSize || name.Any(c => c == 0 || c > 255))
            throw new ArgumentException("Script name must fit the native Latin-1 name field.", nameof(name));
        return Encoding.Latin1.GetBytes(name + '\0');
    }
    public static bool Equal(string a, string b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (Fold(a[i]) != Fold(b[i])) return false;
        return true;
    }
    private static char Fold(char c) => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
}
