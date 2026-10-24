namespace Sacred.Network.Gameplay;

/// <summary>Prevent inflater read-ahead from swallowing a retained packet suffix, so its Adler trailer can be checked.</summary>
internal sealed class GoldProfileZlibInput(byte[] bytes) : MemoryStream(bytes, writable: false)
{
    public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1));
    public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 1)]);
}
