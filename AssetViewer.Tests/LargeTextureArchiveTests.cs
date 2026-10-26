using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Texture;

namespace AssetViewer.Tests;

[TestClass]
public sealed class LargeTextureArchiveTests
{
    [TestMethod]
    public async Task TextureAfterThreeGigabytesIsIndexedAndDecodedWithoutScanningThePayloadGap()
    {
        const uint offset = 3U * 1024 * 1024 * 1024;
        var header = new byte[TexturePakHeaderLayout.SerializedSize + TexturePakDecoder.DescriptorSize];
        "TEX"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(TexturePakHeaderLayout.SerializedSize + 4), offset);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(TexturePakHeaderLayout.SerializedSize + 8), 4);
        var entry = new byte[TexturePakEntryHeaderLayout.SerializedSize + 4];
        Encoding.ASCII.GetBytes("LARGE.TGA").CopyTo(entry, 0);
        var widthOffset = Marshal.OffsetOf<TexturePakEntryHeaderLayout>(nameof(TexturePakEntryHeaderLayout.Width)).ToInt32();
        var heightOffset = Marshal.OffsetOf<TexturePakEntryHeaderLayout>(nameof(TexturePakEntryHeaderLayout.Height)).ToInt32();
        var formatOffset = Marshal.OffsetOf<TexturePakEntryHeaderLayout>(nameof(TexturePakEntryHeaderLayout.StorageFormat)).ToInt32();
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(widthOffset), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(heightOffset), 1);
        entry[formatOffset] = (byte)SacredTextureStorageFormat.Bgra8888;
        new byte[] { 10, 20, 30, 255 }.CopyTo(entry, TexturePakEntryHeaderLayout.SerializedSize);
        using var stream = new SparseArchive(header, offset, entry);
        using var archive = await TexturePakArchive.LoadAsync([("texture.pak", stream)]);
        Assert.AreEqual(1, archive.Entries.Count);
        Assert.AreEqual((long)offset, archive.Entries[0].Record.Offset);
        var texture = await archive.LoadTextureAsync(archive.Entries[0]);
        CollectionAssert.AreEqual(new byte[] { 30, 20, 10, 255 }, texture.Rgba8);
        Assert.IsTrue(stream.BytesRead < 1024, "The archive indexer must only read descriptors, headers and the selected payload.");
    }

    private sealed class SparseArchive(byte[] header, long entryOffset, byte[] entry) : Stream
    {
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => entryOffset + entry.Length;
        public override long Position { get; set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var count = (int)Math.Min(buffer.Length, Length - Position);
            buffer.Span[..count].Clear();
            for (var index = 0; index < count; index++)
            {
                var position = Position + index;
                if (position < header.Length) buffer.Span[index] = header[(int)position];
                if (position >= entryOffset) buffer.Span[index] = entry[(int)(position - entryOffset)];
            }
            Position += count; BytesRead += count; return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Blocking read.");
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        { SeekOrigin.Begin => offset, SeekOrigin.Current => Position + offset, SeekOrigin.End => Length + offset, _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
