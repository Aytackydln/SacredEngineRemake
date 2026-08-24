using System.Runtime.InteropServices;
using System.Text;
using Sacred.Core;
using Sacred.Core.Pak;
using Sacred.Core.Pak.Texture;
using Sacred.Core.Utils;

namespace Sacred.Assets.Paks.Texture;

public static class SacredTextureUnpacker
{
    private static readonly Encoding SacredEncoding = Encoding.GetEncoding("iso-8859-1");

    public static IReadOnlyList<SacredTextureInfo> Extract(
        string pakFilePath)
    {
        if (string.IsNullOrWhiteSpace(pakFilePath))
            throw new ArgumentException("PAK file path cannot be empty.", nameof(pakFilePath));

        using var fs = new FileStream(pakFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs, SacredEncoding);

        var header = br.ReadStruct<TexturePakHeaderLayout>(TexturePakHeaderLayout.SerializedSize);
        header.ValidateSignature();
        if (header.Version != 3)
            throw new InvalidDataException($"Unsupported texture PAK version {header.Version}.");
        var sacredPakFile = new SacredPakFile(pakFilePath, SacredPakFileType.Texture);

        var fileInfos = new TextureFileInfo[checked((int)header.EntryCount)];

        for (var i = 0; i < fileInfos.Length; i++)
        {
            var descriptor = br.ReadStruct<PakEntryDescriptorLayout>(PakEntryDescriptorLayout.SerializedSize);
            fileInfos[i] = new TextureFileInfo(
                TypeId: descriptor.Type,
                Offset: descriptor.Offset,
                CompressedSize: descriptor.Size);
        }

        var extracted = new List<SacredTextureInfo>(fileInfos.Length);

        for (var i = 0; i < fileInfos.Length; i++)
        {
            var fileInfo = fileInfos[i];

            fs.Position = fileInfo.Offset;

            var imageInfo = ReadImageInfo(br);

            if (fileInfo.TypeId != imageInfo.RepeatedTypeId)
                throw new InvalidDataException($"Type mismatch for texture #{i}: {fileInfo.TypeId} != {imageInfo.RepeatedTypeId}");

            if (fileInfo.CompressedSize != imageInfo.RepeatedCompressedSize)
                throw new InvalidDataException($"Compressed size mismatch for texture #{i}: {fileInfo.CompressedSize} != {imageInfo.RepeatedCompressedSize}");

            var dataOffset = fs.Position;

            extracted.Add(new SacredTextureInfo(
                TypeId: fileInfo.TypeId,
                CompressedSize: fileInfo.CompressedSize,
                DataOffset: dataOffset,
                ImageInfo: imageInfo,
                PakFile: sacredPakFile
            ));
        }

        return extracted;
    }

    private static TextureImageInfo ReadImageInfo(BinaryReader br)
    {
        var layout = br.ReadStruct<TexturePakEntryHeaderLayout>(TexturePakEntryHeaderLayout.SerializedSize);
        var fileNameBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref layout, 1))[..0x20];
        var zeroIndex = fileNameBytes.IndexOf((byte)0);

        var fileName = zeroIndex >= 0
            ? SacredEncoding.GetString(fileNameBytes[..zeroIndex])
            : SacredEncoding.GetString(fileNameBytes);

        return new TextureImageInfo(
            FileName: fileName,
            Width: layout.Width,
            Height: layout.Height,
            RepeatedTypeId: (byte)layout.StorageFormat,
            RepeatedCompressedSize: layout.CompressedSize
        );
    }

    private readonly record struct TextureFileInfo(
        uint TypeId,
        uint Offset,
        uint CompressedSize);
}
