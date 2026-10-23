using System.Buffers.Binary;

namespace Sacred.Network.Admission;

/// <summary>Lossless player-information payload; the retained profile is not a character-save decoder.</summary>
public sealed class GoldPlayerData
{
    private readonly byte[] _payload;

    private GoldPlayerData(ReadOnlySpan<byte> payload) => _payload = payload.ToArray();

    public byte CharacterType => _payload[GoldPlayerDataLayout.CharacterTypeOffset];
    public byte ProfileByte5 => _payload[GoldPlayerDataLayout.ProfileByte5Offset];
    public uint ConnectionSpeed => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(GoldPlayerDataLayout.ConnectionSpeedOffset));
    public ushort Version => BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(GoldPlayerDataLayout.VersionOffset));
    public int LobbyIdentity => BinaryPrimitives.ReadInt32LittleEndian(_payload.AsSpan(GoldPlayerDataLayout.LobbyIdentityOffset));
    public string Name => GoldFirstContact.ReadName(_payload.AsSpan(GoldPlayerDataLayout.NameOffset, GoldPlayerDataLayout.NameBytes));
    public ReadOnlySpan<byte> Profile => _payload.AsSpan(GoldPlayerDataLayout.ProfileOffset, GoldPlayerDataLayout.ProfileBytes);

    public static GoldPlayerData FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != GoldPlayerDataLayout.Size)
            throw new InvalidDataException($"Gold player data requires exactly {GoldPlayerDataLayout.Size} payload bytes.");
        return new GoldPlayerData(payload);
    }

    public byte[] ToPayload() => _payload.ToArray();
}
