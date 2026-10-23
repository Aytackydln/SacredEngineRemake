using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Original map-icon/label record: native position, two packed selector bytes and text resource.</summary>
public sealed class GoldMapIconState
{
    private readonly byte[] _data;
    private GoldMapIconState(ReadOnlySpan<byte> data) => _data = data.ToArray();
    public ushort Sector => BinaryPrimitives.ReadUInt16LittleEndian(_data);
    /// <summary>Signed tile coordinates, unlike physical creature positions in units of 1/64 tile.</summary>
    public int X => BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(GoldScriptSpatialLayout.XOffset));
    public int Y => BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(GoldScriptSpatialLayout.YOffset));
    public byte Surface => _data[GoldScriptSpatialLayout.SurfaceOffset];
    public byte Kind => _data[GoldScriptSpatialLayout.KindOrCurvatureOffset];
    public sbyte Curvature => unchecked((sbyte)Kind);
    public sbyte AngleDegrees => unchecked((sbyte)_data[GoldScriptSpatialLayout.LabelAngleOffset]);
    public uint TextResource => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(GoldScriptSpatialLayout.TextResourceOffset));
    /// <summary>Gold getMapNames (0x49A150) selects nonzero resources other than -1.</summary>
    public bool IsLabel => TextResource is not (0 or uint.MaxValue);
    public byte[] ToPayload() => _data.ToArray();
    public static GoldMapIconState FromPayload(ReadOnlySpan<byte> data)
    {
        if (data.Length != GoldScriptSpatialLayout.Size) throw new InvalidDataException("Gold map icons require 24 bytes.");
        return new(data);
    }
}
