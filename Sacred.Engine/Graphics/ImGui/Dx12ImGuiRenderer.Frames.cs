using System;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.ImGui;

internal sealed unsafe partial class Dx12ImGuiRenderer
{
    private sealed class ImGuiFrameResources(ID3D12Device device) : IDisposable
    {
        private ID3D12Resource? _vertexBuffer;
        private ID3D12Resource? _indexBuffer;
        private nint _vertexMapped;
        private nint _indexMapped;
        private int _vertexCapacity;
        private int _indexCapacity;

        public ID3D12Resource? VertexBuffer => _vertexBuffer;
        public ID3D12Resource? IndexBuffer => _indexBuffer;
        public nint VertexMapped => _vertexMapped;
        public nint IndexMapped => _indexMapped;
        public int VertexCapacity => _vertexCapacity;
        public int IndexCapacity => _indexCapacity;

        public void EnsureCapacity(int vertexBytes, int indexBytes)
        {
            EnsureBuffer(ref _vertexBuffer, ref _vertexMapped, ref _vertexCapacity, vertexBytes);
            EnsureBuffer(ref _indexBuffer, ref _indexMapped, ref _indexCapacity, indexBytes);
        }

        private void EnsureBuffer(
            ref ID3D12Resource? buffer,
            ref nint mapped,
            ref int capacity,
            int requiredBytes)
        {
            if (buffer is not null && capacity >= requiredBytes)
                return;

            DisposeBuffer(ref buffer, ref mapped);
            capacity = Math.Max(65_536, RoundUpToPowerOfTwo(requiredBytes));
            var description = new ResourceDescription(
                ResourceDimension.Buffer,
                0,
                (ulong)capacity,
                1,
                1,
                1,
                Format.Unknown,
                1,
                0,
                TextureLayout.RowMajor,
                ResourceFlags.None);
            buffer = device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload, 0, 0),
                HeapFlags.None,
                description,
                ResourceStates.GenericRead,
                null);
            void* pointer;
            buffer.Map(0, null, &pointer).CheckError();
            mapped = (nint)pointer;
        }

        public void Dispose()
        {
            DisposeBuffer(ref _vertexBuffer, ref _vertexMapped);
            DisposeBuffer(ref _indexBuffer, ref _indexMapped);
        }

        private static void DisposeBuffer(ref ID3D12Resource? buffer, ref nint mapped)
        {
            if (buffer is null)
                return;
            buffer.Unmap(0, null);
            buffer.Dispose();
            buffer = null;
            mapped = 0;
        }

        private static int RoundUpToPowerOfTwo(int value)
        {
            var result = 1;
            while (result < value)
                result <<= 1;
            return result;
        }
    }
}
