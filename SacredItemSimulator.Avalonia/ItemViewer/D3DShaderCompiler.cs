using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;

namespace SacredItemSimulator.Avalonia.ItemViewer;

internal static partial class D3DShaderCompiler
{
    [LibraryImport("d3dcompiler_47.dll", StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(AnsiStringMarshaller))]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static partial int D3DCompile(
        nint sourceData,
        nuint sourceDataSize,
        string sourceName,
        nint defines,
        nint include,
        string entryPoint,
        string target,
        uint flags1,
        uint flags2,
        out nint code,
        out nint errorMessages);

    public static ReadOnlyMemory<byte> Compile(string name, string source, string entryPoint, string target)
    {
        var sourceBytes = Encoding.UTF8.GetBytes(source);
        var sourceHandle = GCHandle.Alloc(sourceBytes, GCHandleType.Pinned);
        try
        {
            var result = D3DCompile(
                sourceHandle.AddrOfPinnedObject(),
                (nuint)sourceBytes.Length,
                name,
                0,
                0,
                entryPoint,
                target,
                0,
                0,
                out var code,
                out var error);

            try
            {
                if (result < 0)
                {
                    var message = error != 0 ? ReadBlobString(error) : $"HRESULT 0x{result:X8}";
                    throw new InvalidOperationException($"Failed to compile {name}/{entryPoint}: {message}");
                }

                return ReadBlobBytes(code);
            }
            finally
            {
                if (code != 0)
                    Marshal.Release(code);
                if (error != 0)
                    Marshal.Release(error);
            }
        }
        finally
        {
            sourceHandle.Free();
        }
    }

    private static ReadOnlyMemory<byte> ReadBlobBytes(nint blob)
    {
        var pointer = GetBlobBufferPointer(blob);
        var size = checked((int)GetBlobBufferSize(blob));
        var bytes = new byte[size];
        Marshal.Copy(pointer, bytes, 0, size);
        return bytes;
    }

    private static string ReadBlobString(nint blob)
    {
        var pointer = GetBlobBufferPointer(blob);
        var size = checked((int)GetBlobBufferSize(blob));
        return Marshal.PtrToStringAnsi(pointer, size);
    }

    private static nint GetBlobBufferPointer(nint blob)
    {
        var vtable = Marshal.ReadIntPtr(blob);
        var method = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<GetBufferPointerDelegate>(method)(blob);
    }

    private static nuint GetBlobBufferSize(nint blob)
    {
        var vtable = Marshal.ReadIntPtr(blob);
        var method = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<GetBufferSizeDelegate>(method)(blob);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nint GetBufferPointerDelegate(nint self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nuint GetBufferSizeDelegate(nint self);
}
