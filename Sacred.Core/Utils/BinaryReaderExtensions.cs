using System.Runtime.InteropServices;

namespace Sacred.Core.Utils;

public static class BinaryReaderExtensions
{
    extension(BinaryReader br)
    {
        public T ReadStruct<T>(int byteSize) where T : struct
        {
            var bytes = GC.AllocateUninitializedArray<byte>(byteSize);
            br.ReadExactly(bytes);
            return MemoryMarshal.Read<T>(bytes);
        }
    }
}
