namespace Sacred.Assets;

public interface IPakPayloadReader
{
    ValueTask<byte[]?> TryReadAsync(string path, long offset, int length, CancellationToken cancellationToken = default);
}
