using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace Sacred.Core.Pax.Hero;

internal static class SacredHeroPaxDecompression
{
    public static byte[] Decode(ReadOnlySpan<byte> compressed, int capacity)
    {
        var inflater = new Inflater();
        inflater.SetInput(compressed.ToArray());
        var output = new byte[capacity];
        var overflow = new byte[1];
        var count = 0;
        try
        {
            while (!inflater.IsFinished)
            {
                var full = count == output.Length;
                var read = full ? inflater.Inflate(overflow) : inflater.Inflate(output, count, output.Length - count);
                if (full && read != 0) throw new InvalidDataException("Hero chunk exceeds its declared decoded capacity.");
                count += read;
                if (read == 0 && !inflater.IsFinished) throw new InvalidDataException("Incomplete or unsupported hero zlib stream.");
            }
        }
        catch (SharpZipBaseException error) { throw new InvalidDataException("Corrupt hero zlib stream.", error); }
        // Native uncompress accepts spare capacity and trailing compressed input, returning the actual output length.
        return count == output.Length ? output : output[..count];
    }
}
