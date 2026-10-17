using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

public readonly record struct GoldMovementPoint(int X, int Y);

/// <summary>Immutable, lossless opcode-specific creature movement body. It does not find or simulate paths.</summary>
public sealed class GoldMovementBody
{
    private readonly byte[] _payload;
    private readonly GoldMovementPoint[] _points;
    private GoldMovementBody(ushort opcode, byte[] payload, GoldMovementPoint[] points)
        => (Opcode, _payload, _points) = (opcode, payload, points);

    public ushort Opcode { get; }
    public uint Word1F8 => Word(GoldMovementLayout.Word1F8Offset);
    public uint Flags200 => Word(GoldMovementLayout.Flags200Offset);
    public uint? PathIndex => Opcode == GoldMovementLayout.PathOpcode ? Word(GoldMovementLayout.PathIndexOffset) : null;
    public ReadOnlySpan<GoldMovementPoint> Path => _points;
    public ReadOnlySpan<byte> ActionBlock => Opcode == GoldMovementLayout.ActionOpcode
        ? _payload.AsSpan(GoldMovementLayout.ActionOffset, GoldMovementLayout.ActionSize) : [];
    /// <summary>Native writer selects 0/1/2 for player resources; retain the observed byte without inventing its role.</summary>
    public byte Mode => _payload[^1];
    public byte[] ToPayload() => _payload.ToArray();

    public static GoldMovementBody FromPayload(ushort opcode, ReadOnlySpan<byte> payload)
    {
        if (opcode is not (GoldMovementLayout.PathOpcode or GoldMovementLayout.ActionOpcode))
            throw new ArgumentOutOfRangeException(nameof(opcode));
        var minimum = opcode == GoldMovementLayout.PathOpcode ? GoldMovementLayout.PathOffset + 1 : GoldMovementLayout.ActionBodySize;
        if (payload.Length < minimum || BinaryPrimitives.ReadUInt32LittleEndian(payload) != GoldMovementLayout.Marker)
            throw new InvalidDataException("Invalid Gold movement body marker or size.");
        var points = Array.Empty<GoldMovementPoint>();
        if (opcode == GoldMovementLayout.PathOpcode)
        {
            var count = BinaryPrimitives.ReadUInt32LittleEndian(payload[GoldMovementLayout.PathCountOffset..]);
            if (GoldMovementLayout.PathOffset + (long)count * GoldMovementLayout.PathPointSize + 1 != payload.Length)
                throw new InvalidDataException("Gold movement path count does not match its complete body.");
            points = new GoldMovementPoint[(int)count];
            for (var i = 0; i < points.Length; i++)
            {
                var point = payload[(GoldMovementLayout.PathOffset + i * GoldMovementLayout.PathPointSize)..];
                points[i] = new(BinaryPrimitives.ReadInt32LittleEndian(point), BinaryPrimitives.ReadInt32LittleEndian(point[4..]));
            }
        }
        else if (payload.Length != GoldMovementLayout.ActionBodySize)
            throw new InvalidDataException("Gold fixed action body has trailing bytes.");
        return new(opcode, payload.ToArray(), points);
    }

    private uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(offset));
}
