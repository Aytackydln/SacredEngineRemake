namespace SacredItemSimulator.Utils;

public static class ByteArrayUtils
{
    public static byte[] Combine(ReadOnlySpan<byte> array1, ReadOnlySpan<byte> array2)
    {
        return Combine(array1, array2, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);
    }
    
    public static byte[] Combine(ReadOnlySpan<byte> array1, ReadOnlySpan<byte> array2, ReadOnlySpan<byte> array3)
    {
        return Combine(array1, array2, array3, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty);
    }

    public static byte[] Combine(ReadOnlySpan<byte> array1, ReadOnlySpan<byte> array2, ReadOnlySpan<byte> array3, ReadOnlySpan<byte> array4)
    {
        return Combine(array1, array2, array3, array4, ReadOnlySpan<byte>.Empty);
    }

    public static byte[] Combine(ReadOnlySpan<byte> array1, ReadOnlySpan<byte> array2, ReadOnlySpan<byte> array3, ReadOnlySpan<byte> array4, ReadOnlySpan<byte> array5)
    {
        var totalLength = array1.Length + array2.Length + array3.Length + array4.Length + array5.Length;
        var result = new byte[totalLength];
        var offset = 0;
        array1.CopyTo(result.AsSpan(offset));
        offset += array1.Length;
        array2.CopyTo(result.AsSpan(offset));
        offset += array2.Length;
        array3.CopyTo(result.AsSpan(offset));
        offset += array3.Length;
        array4.CopyTo(result.AsSpan(offset));
        offset += array4.Length;
        array5.CopyTo(result.AsSpan(offset));
        return result;
    }
}