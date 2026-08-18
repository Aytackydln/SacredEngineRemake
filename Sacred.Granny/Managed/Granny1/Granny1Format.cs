using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Sacred.Granny.Meshes;

namespace Sacred.Granny.Managed.Granny1;

public static partial class Granny1MeshExtractor
{
    private static List<MeshSurface> BuildSurfaceRanges(IReadOnlyList<TexturePolygonBlock> blocks, IReadOnlyList<string> textureNames)
    {
        var surfaces = new List<MeshSurface>(blocks.Count);
        var polygonStart = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            var polygonCount = blocks[i].Polygons.Length;
            if (polygonCount > 0)
                surfaces.Add(new MeshSurface(
                    polygonStart * 3,
                    polygonCount * 3,
                    TextureNameForBlock(blocks[i], i, textureNames)));

            polygonStart += polygonCount;
        }

        return surfaces;
    }

    private static MeshSurface[] ClipSurfaceRanges(IReadOnlyList<MeshSurface> surfaces, int indexCount)
    {
        if (surfaces.Count == 0 || indexCount <= 0)
            return [];

        var clipped = new List<MeshSurface>(surfaces.Count);
        var nextIndex = 0;
        foreach (var surface in surfaces)
        {
            if (surface.IndexStart >= indexCount)
                continue;

            if (surface.IndexStart > nextIndex)
                clipped.Add(new MeshSurface(nextIndex, surface.IndexStart - nextIndex, null));

            var count = Math.Min(surface.IndexCount, indexCount - surface.IndexStart);
            if (count > 0)
            {
                clipped.Add(surface with { IndexCount = count });
                nextIndex = surface.IndexStart + count;
            }
        }

        if (nextIndex < indexCount)
            clipped.Add(new MeshSurface(nextIndex, indexCount - nextIndex, null));

        return clipped.ToArray();
    }

    private static List<string> ExtractTextureNames(ReadOnlySpan<byte> data)
    {
        var names = new List<string>();
        for (var offset = 0; offset < data.Length;)
        {
            var extensionOffset = IndexOfTextureExtension(data[offset..]);
            if (extensionOffset < 0)
                break;

            extensionOffset += offset;
            var start = extensionOffset;
            while (start > 0 && IsTexturePathByte(data[start - 1]))
                start--;

            var end = extensionOffset + 4;
            if (end > start)
            {
                var raw = Encoding.Latin1.GetString(data[start..end]).Replace('\\', '/');
                var slash = raw.LastIndexOf('/');
                var name = slash >= 0 ? raw[(slash + 1)..] : raw;
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
            }

            offset = end;
        }

        return names;
    }

    private static int IndexOfTextureExtension(ReadOnlySpan<byte> data)
    {
        for (var i = 0; i + 4 <= data.Length; i++)
        {
            if (data[i] != (byte)'.')
                continue;

            if (ToLowerAscii(data[i + 1]) == (byte)'t' &&
                ToLowerAscii(data[i + 2]) == (byte)'g' &&
                ToLowerAscii(data[i + 3]) == (byte)'a')
                return i;
        }

        return -1;
    }

    private static bool IsTexturePathByte(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' ||
        value is >= (byte)'a' and <= (byte)'z' ||
        value is >= (byte)'0' and <= (byte)'9' ||
        value is (byte)'_' or (byte)'-' or (byte)'.' or (byte)'/' or (byte)'\\' or (byte)':';

    private static byte ToLowerAscii(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;

    private static Bounds CalculateBounds(IEnumerable<ParsedMeshPart> parts)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var part in parts)
        {
            foreach (var position in part.Positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }
        }

        return new Bounds(min, max);
    }

    private static List<int> FindGrannySliceStarts(ReadOnlySpan<byte> data)
    {
        var starts = new List<int>();
        for (var offset = HeaderSize; offset + 8 <= data.Length; offset++)
        {
            if (ReadUInt32(data, offset) != MainChunk || !LooksLikeGrannyMainChunk(data, offset))
                continue;

            var start = offset - HeaderSize;
            if (starts.Count == 0 || starts[^1] != start)
                starts.Add(start);
        }

        return starts;
    }

    private static bool LooksLikeGrannyMainChunk(ReadOnlySpan<byte> data, int mainChunkOffset)
    {
        var childCount = ReadUInt32(data, mainChunkOffset + 4);
        if (childCount == 0 || childCount > 16)
            return false;

        var descriptorOffset = mainChunkOffset + 4 + 4 + 24;
        var hasObject = false;
        for (var child = 0; child < childCount; child++)
        {
            if (descriptorOffset + 20 > data.Length)
                return false;

            var chunk = ReadUInt32(data, descriptorOffset);
            if (chunk is not FinalChunk and not CopyrightChunk and not ObjectChunk)
                return false;

            hasObject |= chunk == ObjectChunk;
            descriptorOffset += 20;
        }

        return hasObject;
    }

    private static IEnumerable<(int Start, int End)> EnumerateSlices(IReadOnlyList<int> starts, int length)
    {
        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i];
            var end = i + 1 < starts.Count ? starts[i + 1] : length;
            if (start >= 0 && end > start)
                yield return (start, end);
        }
    }

    private static bool OffsetsAreValid(int length, params int[] offsets)
    {
        for (var i = 0; i < offsets.Length; i++)
        {
            if (offsets[i] < 0 || offsets[i] >= length)
                return false;

            if (i > 0 && offsets[i] <= offsets[i - 1])
                return false;
        }

        return true;
    }

    private static int AddOffset(int baseOffset, uint relativeOffset)
    {
        var absolute = baseOffset + (long)relativeOffset;
        return absolute is >= 0 and <= int.MaxValue ? (int)absolute : -1;
    }

    private static int VerticalAxis(Vector3 span) =>
        span.X >= span.Y && span.X >= span.Z
            ? 0
            : span.Y >= span.Z ? 1 : 2;

    private static float Axis(Vector3 value, int axis) =>
        axis switch
        {
            0 => value.X,
            1 => value.Y,
            _ => value.Z
        };

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static Vector3 NormalizeOrZero(Vector3 value) =>
        IsFinite(value) && value.LengthSquared() > 0.000001f
            ? Vector3.Normalize(value)
            : Vector3.Zero;

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private static bool IsFinite(Matrix4x4 value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
        float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
        float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
        float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));

    private static float ReadSingle(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.ToSingle(data.Slice(offset, 4));
}

