using System;
using System.Runtime.InteropServices;
using Sacred.Engine.Extern;

namespace Sacred.Engine.Graphics;

public static class Dx12ShaderCompiler
{
    internal static unsafe ReadOnlyMemory<byte> CompileShader(Dx12Shader shader)
    {
        var sourceBytes = shader.ReadAllBytes();
        var sourceName = shader.Name;
        var entryPoint = shader.ShaderEntry;
        var target = shader.ShaderTarget;

        int result;
        nint code;
        nint error;
        fixed (byte* sourceData = sourceBytes)
        {
            result = D3DCompiler.D3DCompile(
                sourceData,
                (nuint)sourceBytes.Length,
                sourceName,
                IntPtr.Zero,
                IntPtr.Zero,
                entryPoint,
                target,
                0,
                0,
                out code,
                out error);
        }

        try
        {
            if (result < 0)
            {
                var message = error != IntPtr.Zero ? ReadBlobString(error) : $"HRESULT 0x{result:X8}";
                throw new InvalidOperationException($"Failed to compile {entryPoint}: {message}");
            }

            return ReadBlobBytes(code);
        }
        finally
        {
            if (code != IntPtr.Zero)
                Marshal.Release(code);
            if (error != IntPtr.Zero)
                Marshal.Release(error);
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
