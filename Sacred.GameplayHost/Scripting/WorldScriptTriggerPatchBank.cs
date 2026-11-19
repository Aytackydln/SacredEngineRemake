using System.Buffers.Binary;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 7E891B AVL flattening and 7E8C87 bank-3 section-2 writer.
/// Only the host's established packed-cell/ref table, in unsigned key order.
/// Pending tile positions and the world trigger vector are separate sources.</summary>
internal static class WorldScriptTriggerPatchBank
{
    public static byte[] Write(IReadOnlyDictionary<uint,uint> patches)
    {
        var bytes = new byte[checked(patches.Count * 8)];
        var offset = 0;
        foreach (var pair in patches.OrderBy(pair => pair.Key))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset),pair.Key);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4),pair.Value);
            offset += 8;
        }
        return bytes;
    }
}
