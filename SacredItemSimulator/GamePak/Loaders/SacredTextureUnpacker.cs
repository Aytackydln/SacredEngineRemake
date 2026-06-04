using System.Text;
using Sacred.Core.Texture;

namespace SacredItemSimulator.GamePak.Loaders;

public static class SacredTextureUnpacker
{
    private const int MainHeaderPaddingSize = 244;
    private const int TextureNameSize = 32;
    private const int ImageInfoPaddingSize = 39;

    private static readonly Encoding SacredEncoding = Encoding.GetEncoding("iso-8859-1");

    public static IReadOnlyList<SacredTextureInfo> Extract(
        string pakFilePath)
    {
        if (string.IsNullOrWhiteSpace(pakFilePath))
            throw new ArgumentException("PAK file path cannot be empty.", nameof(pakFilePath));

        using var fs = new FileStream(pakFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs, SacredEncoding);

        var signature = SacredEncoding.GetString(br.ReadBytes(3));
        var version = br.ReadByte();

        if (!signature.Equals("TEX", StringComparison.OrdinalIgnoreCase) || version != 3)
            throw new InvalidDataException($"Unsupported texture PAK format. Signature={signature}, Version={version}");
        var sacredPakFile = new SacredPakFile(pakFilePath, SacredPakFileType.Texture);

        var fileCount = br.ReadUInt32();
        var unknown = br.ReadUInt32();

        br.ReadBytes(MainHeaderPaddingSize);

        var fileInfos = new TextureFileInfo[fileCount];

        for (var i = 0; i < fileInfos.Length; i++)
        {
            fileInfos[i] = new TextureFileInfo(
                TypeId: br.ReadUInt32(),
                Offset: br.ReadUInt32(),
                CompressedSize: br.ReadUInt32());
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
        var fileNameBytes = br.ReadBytes(TextureNameSize);
        var zeroIndex = Array.IndexOf(fileNameBytes, (byte)0);

        var fileName = zeroIndex >= 0
            ? SacredEncoding.GetString(fileNameBytes, 0, zeroIndex)
            : SacredEncoding.GetString(fileNameBytes);

        var width = br.ReadUInt16();
        var height = br.ReadUInt16();
        var repeatedTypeId = br.ReadByte();
        var repeatedCompressedSize = br.ReadUInt32();

        var padding = br.ReadBytes(ImageInfoPaddingSize);

        return new TextureImageInfo(
            FileName: fileName,
            Width: width,
            Height: height,
            RepeatedTypeId: repeatedTypeId,
            RepeatedCompressedSize: repeatedCompressedSize
        );
    }

    private readonly record struct TextureFileInfo(
        uint TypeId,
        uint Offset,
        uint CompressedSize);
}