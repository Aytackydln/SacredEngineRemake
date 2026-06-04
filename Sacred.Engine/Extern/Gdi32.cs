using System.Runtime.InteropServices;

namespace Sacred.Engine.Extern;

internal static partial class Gdi32
{
    private const string LibraryName = "gdi32";

    [LibraryImport(LibraryName)]
    internal static partial nint GetStockObject(int index);
}