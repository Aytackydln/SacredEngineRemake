using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Last correction received while native creature flag 0x20000 is set.</summary>
public sealed class GoldDeferredPosition
{
    internal GoldDeferredPosition(GoldObjectPositionEvent correction)
    {
        Position = correction.Position with { Sector = unchecked((ushort)correction.Position.Sector), Surface = unchecked((byte)correction.Position.Surface) };
        FacingBits = correction.FacingBits;
    }
    public GoldObjectPosition Position { get; }
    public uint FacingBits { get; }
    /// <summary>Exact zero-padded native slot, including its presence byte; not a wire packet.</summary>
    public byte[] ToPayload()
    {
        var bytes = new byte[GoldDeferredPositionLayout.Size];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldDeferredPositionLayout.SectorOffset), (ushort)Position.Sector);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(GoldDeferredPositionLayout.XOffset), Position.X);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(GoldDeferredPositionLayout.YOffset), Position.Y);
        bytes[GoldDeferredPositionLayout.SurfaceOffset] = (byte)Position.Surface;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldDeferredPositionLayout.FacingOffset), FacingBits);
        bytes[GoldDeferredPositionLayout.PresentOffset] = 1;
        return bytes;
    }
}
