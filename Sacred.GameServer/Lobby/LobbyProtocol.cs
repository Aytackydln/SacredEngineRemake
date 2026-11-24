using System.Buffers.Binary;
using System.Text;

namespace Sacred.GameServer.Lobby;

/// <summary>
/// Lobby messages a game server exchanges with the lobby (module 0x26B6). Layouts are from GameServer.exe: login
/// <c>FUN_0048c650</c> (12), update <c>FUN_0048c8d0</c> (13), logout <c>FUN_0048ca70</c> (14), player announce
/// <c>FUN_0048d130</c> (45), player removed <c>FUN_0048d1c0</c> (46); the receive switch is at <c>0x48ED40</c>.
/// A TinCat CUSTOMDATA payload is <c>module u16 | type u16 | envelope (type u16, key u32, length u32, checksum u32) | body</c>.
/// </summary>
public static class LobbyProtocol
{
    public const ushort Module = 0x26B6;
    public const int EnvelopeBytes = 14;
    public const int PrefixBytes = 4 + EnvelopeBytes;

    public const ushort ServerDataUpload = 18, ServerDataRequest = 19;
    public const int MaxBlock = 0x20000;          // sacred-lobby proto.MaxData: the most one data message carries

    public const ushort PublicData = 11, ServerLogin = 12, ServerUpdate = 13, ServerLogout = 14, Result = 15, ServerLoginResult = 38,
        UserJoinedServer = 45, UserLeftServer = 46, UserKicked = 47, ClientImportant = 48, ServerImportant = 49, ServerShutdown = 50;

    /// <summary>Body of messages 12/13/14 (the 0x82-byte native message minus the envelope).</summary>
    public const int GameRecordBytes = 0x82 - EnvelopeBytes;
    public const int NetworkVersion = 10;           // FUN_0048c650 writes 10 at record offset 104
    public const int OpenNetChannel = 5;            // ... and channel 5 at offset 112

    /// <summary>Writes the module/type prefix and envelope for <paramref name="bodyLength"/> body bytes at <c>destination[0..18]</c>.</summary>
    public static void WritePrefix(Span<byte> destination, ushort type, int bodyLength)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, Module);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], type);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], type);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[6..], KeyOf(type));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[10..], (uint)(EnvelopeBytes + bodyLength));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[14..], 0); // checksum: the native peers accept 0
    }

    /// <summary>Envelope keys of the types this server sends or reads (rows of the native table).</summary>
    public static uint KeyOf(ushort type) => type switch
    {
        <= 9 => 0xDDCCBB00u + type,
        <= 19 => 0xDDCCBB10u + (uint)(type - 10),
        <= 39 => 0xDDCCBB1Au + (uint)(type - 20),
        _ => 0xDDCCBB2Eu + (uint)(type - 45),
    };

    /// <summary>Game record of messages 12/13 (116 bytes).</summary>
    public readonly record struct GameRecord(string Name, uint LanAddress, uint PublicAddress, int Port, int Players, int MaxPlayers,
        uint Flags, int ClientVersion, int Channel);

    /// <summary>Message 12 body: name[80] cp1252, LAN IP, public IP (network order), port, players, max, flags, id, versions, channel.</summary>
    public static void WriteLogin(Span<byte> body, in GameRecord g)
    {
        body[..GameRecordBytes].Clear();
        WriteName(body, g.Name);
        BinaryPrimitives.WriteUInt32BigEndian(body[80..], g.LanAddress);
        BinaryPrimitives.WriteUInt32BigEndian(body[84..], g.PublicAddress);
        BinaryPrimitives.WriteInt32LittleEndian(body[88..], g.Port);
        BinaryPrimitives.WriteInt16LittleEndian(body[92..], (short)g.Players);
        BinaryPrimitives.WriteInt16LittleEndian(body[94..], (short)g.MaxPlayers);
        BinaryPrimitives.WriteUInt32LittleEndian(body[96..], g.Flags);
        BinaryPrimitives.WriteInt32LittleEndian(body[104..], NetworkVersion);
        BinaryPrimitives.WriteInt32LittleEndian(body[108..], g.ClientVersion);
        BinaryPrimitives.WriteInt32LittleEndian(body[112..], g.Channel);
    }

    /// <summary>Message 13 body: only name, players, max and flags are filled (FUN_0048c8d0).</summary>
    public static void WriteUpdate(Span<byte> body, in GameRecord g)
    {
        body[..GameRecordBytes].Clear();
        WriteName(body, g.Name);
        BinaryPrimitives.WriteInt16LittleEndian(body[92..], (short)g.Players);
        BinaryPrimitives.WriteInt16LittleEndian(body[94..], (short)g.MaxPlayers);
        BinaryPrimitives.WriteUInt32LittleEndian(body[96..], g.Flags);
    }

    /// <summary>Message 45 body (8 bytes): perm id, character block, 1 on the first announcement and 0 on re-announcements.</summary>
    public static void WriteUserJoined(Span<byte> body, int permId, ushort block, bool first)
    {
        BinaryPrimitives.WriteInt32LittleEndian(body, permId);
        BinaryPrimitives.WriteUInt16LittleEndian(body[4..], block);
        BinaryPrimitives.WriteUInt16LittleEndian(body[6..], first ? (ushort)1 : (ushort)0);
    }

    /// <summary>Message 19 body (14 bytes), a game server asking for a player's data block: perm id, block, offset, length
    /// (GameServer.exe asks for offset 0, length 0x20000 = the whole block; sacred-lobby answers with one PublicData message 11).</summary>
    public static void WriteDataRequest(Span<byte> body, int permId, ushort block, int offset = 0, int length = 0)
    {
        BinaryPrimitives.WriteInt32LittleEndian(body, permId);
        BinaryPrimitives.WriteUInt16LittleEndian(body[4..], block);
        BinaryPrimitives.WriteInt32LittleEndian(body[6..], offset);
        BinaryPrimitives.WriteInt32LittleEndian(body[10..], length);
    }

    /// <summary>Message 18 body, a game server storing a player's character block: perm id, block, size, offset 0, length, data.</summary>
    public static void WriteDataUpload(Span<byte> body, int permId, ushort block, ReadOnlySpan<byte> data)
    {
        BinaryPrimitives.WriteInt32LittleEndian(body, permId);
        BinaryPrimitives.WriteUInt16LittleEndian(body[4..], block);
        BinaryPrimitives.WriteInt32LittleEndian(body[6..], data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(body[10..], 0);
        BinaryPrimitives.WriteInt32LittleEndian(body[14..], data.Length);
        data.CopyTo(body[18..]);
    }

    private static void WriteName(Span<byte> body, string name)
    {
        var n = Math.Min(name.Length, 0x4F); // strncpy(..., 0x4F)
        for (var i = 0; i < n; i++) body[i] = name[i] <= 0xFF ? (byte)name[i] : (byte)'?';
    }

    /// <summary>Reads a zero-terminated cp1252/Latin-1 text field.</summary>
    public static string Text(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? field : field[..end]);
    }
}
