using System.Buffers.Binary;

namespace Sacred.GameServer.Auth.Hero;

/// <summary>
/// Chunk CD (decoded type CC, Sacred.Core names it "Scripts"): despite the name it holds no quest or script state but the
/// hero's explored map, one 32x32 bit mask per world area. Verified in Sacred.exe (Gold, GOG):
/// <list type="bullet">
/// <item>Writer 0x605396..0x60545E: <c>[0xB00BB00B][u32 area count][0][0]</c>, then per area index i
/// <c>[u32 i][u32 0]</c> + 128 bytes from area +0x180 (0x622B80), or <c>[u32 i][0xDEADB00B]</c> when the world has no area i;
/// area 0's first dword is written as 0x20202020. Reader 0x606901 (save version &gt;= 0x1A) copies each block back with
/// 0x622BA0, which also sets area flag 0x1000 (+0x30) and counts the area in cStatsManager (0x4249C0) when it is not empty.</item>
/// <item>The mask is set by 0x622B10 (bit ((x - area x0) / 2, (y - area y0) / 2), called through 0x638E00 from 0x5997C0):
/// a creature method that acts only for the local player's hero (owner chain +0x1EC/+0xC against the local player id),
/// whenever its tile position changes, revealing the radius tables at 0x8915CC (29x29) / 0x891328 (15x15 by size).</item>
/// <item>No message carries the masks: the only callers of the block copy-out 0x622B80 are the save writer and the map
/// renderer (0x6CE109), the merge 0x622C10 has no caller, and none of these addresses occurs as a constant in the image.</item>
/// </list>
/// The masks are therefore only known to the client (they follow every tile step of the local hero and need the world's
/// area grid +0x284); the server keeps chunk CD exactly as delivered. Real Gold saves: 6051 areas (0..6050), area 0 absent,
/// 822824 bytes decoded.
/// </summary>
public static class ExploredMap
{
    public const uint Marker = 0xB00BB00B, AbsentArea = 0xDEADB00B;
    public const int HeaderBytes = 16, EntryBytes = 8, MaskBytes = 0x80;

    /// <summary>Checks the layout of a decoded CC chunk; returns the area count or -1.</summary>
    public static int Validate(ReadOnlySpan<byte> chunk, out int present, out int explored)
    {
        present = explored = 0;
        if (chunk.Length < HeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(chunk) != Marker) return -1;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
        var position = HeaderBytes;
        for (uint i = 0; i < count; i++)
        {
            if (position + EntryBytes > chunk.Length || BinaryPrimitives.ReadUInt32LittleEndian(chunk[position..]) != i) return -1;
            var flag = BinaryPrimitives.ReadUInt32LittleEndian(chunk[(position + 4)..]);
            position += EntryBytes;
            if (flag == AbsentArea) continue;
            if (flag != 0 || position + MaskBytes > chunk.Length) return -1;
            present++;
            if (i != 0 && chunk.Slice(position, MaskBytes).IndexOfAnyExcept((byte)0) >= 0) explored++;   // area 0's dword is forced
            position += MaskBytes;
        }
        return position == chunk.Length ? (int)count : -1;
    }
}
