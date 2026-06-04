using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Sacred.Engine.Extern;

internal static partial class D3DCompiler
{
    private const string D3DCompilerDll = "d3dcompiler_47.dll";

    [LibraryImport(D3DCompilerDll, StringMarshalling = StringMarshalling.Custom,
        StringMarshallingCustomType = typeof(AnsiStringMarshaller))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    internal static unsafe partial int D3DCompile(
        byte* sourceData,
        nuint sourceDataSize,
        string sourceName,
        IntPtr defines,
        IntPtr include,
        string entryPoint,
        string target,
        uint flags1,
        uint flags2,
        out IntPtr code,
        out IntPtr errorMessages);
}