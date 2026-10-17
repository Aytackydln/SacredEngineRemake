using System.Buffers.Binary;
using Sacred.Core.World;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold participant/host upload controls. These messages do not serialize or create a hero.</summary>
public static class GoldWorldEntryCodec
{
    public const ushort ParticipantUploadOpcode = 0x15;
    public const ushort HostUploadOpcode = 0x16;
    public const ushort ParticipantStartOpcode = 0x17;
    public const ushort HostStartOpcode = 0x18;
    public const ushort PeerReadyOpcode = 0x0F;
    /// <summary>Gold GameServer 0x4C4C86: request its four current world banks for this participant.</summary>
    public const ushort WorldBankRequestOpcode = 0x1B5;
    public const byte AssignedHostFlag = 1;
    public const byte HostWorldAvailableFlag = 4;

    public static GoldMessage CreateParticipantUpload(byte playerSlot) => Empty(ParticipantUploadOpcode, playerSlot);
    /// <summary>Send only after the native participant's hero/inventory upload is complete.</summary>
    public static GoldMessage CreateParticipantStart(byte playerSlot) => Empty(ParticipantStartOpcode, playerSlot);
    public static GoldMessage CreatePeerReady(byte playerSlot) => Empty(PeerReadyOpcode, playerSlot);
    public static GoldMessage CreateWorldBankRequest(byte playerSlot) => Empty(WorldBankRequestOpcode, playerSlot);
    public static GoldMessage CreateHostUpload(byte playerSlot) => Empty(HostUploadOpcode, playerSlot);

    /// <summary>Native host finish sends the sector runtime table size, including its reserved zero slot.</summary>
    public static GoldMessage CreateHostStart(byte playerSlot, SacredSectorIndex sectors)
    {
        ArgumentNullException.ThrowIfNull(sectors);
        return CreateHostStart(playerSlot, sectors.RuntimeTableSize);
    }

    /// <summary>Low-level producer retaining the raw table-size word, including values from packet captures.</summary>
    public static GoldMessage CreateHostStart(byte playerSlot, uint word)
    {
        RequireSlot(playerSlot);
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, word);
        return new(HostStartOpcode, playerSlot, playerSlot, 0, payload);
    }

    private static GoldMessage Empty(ushort opcode, byte playerSlot)
    {
        RequireSlot(playerSlot);
        return new(opcode, playerSlot, playerSlot, 0, []);
    }

    private static void RequireSlot(byte playerSlot)
    {
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
    }
}
