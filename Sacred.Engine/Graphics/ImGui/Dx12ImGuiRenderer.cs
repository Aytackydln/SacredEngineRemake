using System;
using System.IO;
using System.Numerics;
using ImGuiNET;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Platform;
using Sacred.Shaders;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Feeds engine input to Dear ImGui and records its draw data into the active DX12 frame.</summary>
internal sealed unsafe class Dx12ImGuiRenderer : IDisposable
{
    private const int VertexStride = 20;
    private const int IndexStride = sizeof(ushort);

    private readonly ID3D12Device _device;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly Dx12TextureUploader _textureUploader;
    private readonly InputState _input;
    private readonly CpuDescriptorHandle _fontCpuHandle;
    private readonly GpuDescriptorHandle _srvHeapGpuStart;
    private readonly int _srvDescriptorSize;
    private readonly int _fontSrvSlot;
    private readonly ImGuiFrameResources[] _frames;
    private readonly nint _context;
    private readonly byte[] _fontPixels;
    private readonly int _fontWidth;
    private readonly int _fontHeight;

    private ID3D12Resource? _fontTexture;
    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _pipeline;
    private bool _frameBegun;

    public bool IsFrameBegun => _frameBegun;

    public Dx12ImGuiRenderer(
        ID3D12Device device,
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader textureUploader,
        ID3D12DescriptorHeap srvHeap,
        int srvDescriptorSize,
        int fontSrvSlot,
        int frameCount,
        InputState input)
    {
        _device = device;
        _commandList = commandList;
        _textureUploader = textureUploader;
        _input = input;
        _fontSrvSlot = fontSrvSlot;
        _srvDescriptorSize = srvDescriptorSize;
        _fontCpuHandle = srvHeap.GetCPUDescriptorHandleForHeapStart() + fontSrvSlot * srvDescriptorSize;
        _srvHeapGpuStart = srvHeap.GetGPUDescriptorHandleForHeapStart();
        _frames = new ImGuiFrameResources[frameCount];
        for (var index = 0; index < _frames.Length; index++)
            _frames[index] = new ImGuiFrameResources(device);

        _context = ImGuiNET.ImGui.CreateContext();
        ImGuiNET.ImGui.SetCurrentContext(_context);
        ConfigureStyle();
        ConfigureFonts();

        var io = ImGuiNET.ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.NativePtr->IniFilename = null;
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out var width, out var height, out var bytesPerPixel);
        _fontWidth = width;
        _fontHeight = height;
        _fontPixels = new byte[checked(width * height * bytesPerPixel)];
        fixed (byte* destination = _fontPixels)
            Buffer.MemoryCopy(pixels, destination, _fontPixels.Length, _fontPixels.Length);
        io.Fonts.SetTexID((nint)fontSrvSlot);
        io.Fonts.ClearTexData();
    }

    public ImFontPtr TitleFont { get; private set; }
    public ImFontPtr BodyFont { get; private set; }

    public void SetPipeline(Dx12CreatedPipelineGroup pipeline)
    {
        _rootSignature = pipeline.RootSignature;
        _pipeline = pipeline[Dx12PipelineKind.ImGui];
    }

    public void DisposePipeline()
    {
        _pipeline?.Dispose();
        _pipeline = null;
        _rootSignature?.Dispose();
        _rootSignature = null;
    }

    public void BeginFrame(float deltaSeconds, int renderWidth, int renderHeight)
    {
        if (_frameBegun)
            throw new InvalidOperationException("The previous ImGui frame was not completed.");

        ImGuiNET.ImGui.SetCurrentContext(_context);
        var io = ImGuiNET.ImGui.GetIO();
        io.DisplaySize = new Vector2(renderWidth, renderHeight);
        io.DisplayFramebufferScale = Vector2.One;
        io.DeltaTime = Math.Max(deltaSeconds, 1.0f / 1000.0f);
        io.AddMousePosEvent(_input.MousePosition.X, _input.MousePosition.Y);
        io.AddMouseButtonEvent(0, _input.IsLeftMouseButtonDown);
        io.AddMouseButtonEvent(1, _input.IsRightMouseButtonDown);
        io.AddMouseButtonEvent(2, _input.IsMiddleMouseButtonDown);
        io.AddMouseWheelEvent(0.0f, _input.MouseWheelDelta / 120.0f);
        AddKeyboardEvents(io);

        ImGuiNET.ImGui.NewFrame();
        _frameBegun = true;
        _input.SetUiCapture(io.WantCaptureMouse, io.WantCaptureKeyboard);
    }

    public void DiscardFrame()
    {
        if (!_frameBegun)
            return;

        ImGuiNET.ImGui.SetCurrentContext(_context);
        ImGuiNET.ImGui.EndFrame();
        _frameBegun = false;
        _input.SetUiCapture(false, false);
    }

    public void Record(Dx12FrameContext frame, float uiPaperWhiteNits)
    {
        if (!_frameBegun)
            return;

        EnsureFontTexture(frame);
        ImGuiNET.ImGui.SetCurrentContext(_context);
        ImGuiNET.ImGui.Render();
        _frameBegun = false;

        var drawData = ImGuiNET.ImGui.GetDrawData();
        if (drawData.CmdListsCount == 0 || drawData.DisplaySize.X <= 0.0f || drawData.DisplaySize.Y <= 0.0f)
            return;

        var resources = _frames[frame.Index];
        resources.EnsureCapacity(drawData.TotalVtxCount * VertexStride, drawData.TotalIdxCount * IndexStride);
        CopyDrawData(drawData, resources);
        RecordDrawData(drawData, resources, uiPaperWhiteNits);
        _input.SetUiCapture(ImGuiNET.ImGui.GetIO().WantCaptureMouse, ImGuiNET.ImGui.GetIO().WantCaptureKeyboard);
    }

    private void EnsureFontTexture(Dx12FrameContext frame)
    {
        if (_fontTexture is not null)
            return;

        _fontTexture = _textureUploader.UploadRgbaTexture(
            _commandList,
            _fontWidth,
            _fontHeight,
            _fontPixels,
            frame.TransientResources);
        _textureUploader.CreateShaderResourceView(_fontTexture, _fontCpuHandle);
    }

    private static void CopyDrawData(ImDrawDataPtr drawData, ImGuiFrameResources resources)
    {
        var vertexOffset = 0;
        var indexOffset = 0;
        for (var listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            var drawList = drawData.CmdLists[listIndex];
            var vertexBytes = drawList.VtxBuffer.Size * VertexStride;
            var indexBytes = drawList.IdxBuffer.Size * IndexStride;
            Buffer.MemoryCopy(
                (void*)drawList.VtxBuffer.Data,
                (byte*)resources.VertexMapped + vertexOffset,
                resources.VertexCapacity - vertexOffset,
                vertexBytes);
            Buffer.MemoryCopy(
                (void*)drawList.IdxBuffer.Data,
                (byte*)resources.IndexMapped + indexOffset,
                resources.IndexCapacity - indexOffset,
                indexBytes);
            vertexOffset += vertexBytes;
            indexOffset += indexBytes;
        }
    }

    private void RecordDrawData(
        ImDrawDataPtr drawData,
        ImGuiFrameResources resources,
        float uiPaperWhiteNits)
    {
        if (_rootSignature is null || _pipeline is null)
            throw new InvalidOperationException("The ImGui DX12 pipeline has not been assigned.");

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_pipeline);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        var constants = stackalloc float[ImGuiShaderLayout.ConstantsCount]
        {
            2.0f / drawData.DisplaySize.X,
            -2.0f / drawData.DisplaySize.Y,
            -1.0f - drawData.DisplayPos.X * (2.0f / drawData.DisplaySize.X),
            1.0f + drawData.DisplayPos.Y * (2.0f / drawData.DisplaySize.Y),
            uiPaperWhiteNits
        };
        _commandList.SetGraphicsRoot32BitConstants(
            ImGuiShaderLayout.ConstantsRootParameter,
            ImGuiShaderLayout.ConstantsCount,
            constants,
            0);

        var vertexView = new VertexBufferView(
            resources.VertexBuffer!.GPUVirtualAddress,
            (uint)(drawData.TotalVtxCount * VertexStride),
            VertexStride);
        var indexView = new IndexBufferView(
            resources.IndexBuffer!.GPUVirtualAddress,
            (uint)(drawData.TotalIdxCount * IndexStride),
            Format.R16_UInt);
        _commandList.IASetVertexBuffers(0, 1, &vertexView);
        _commandList.IASetIndexBuffer(&indexView);

        var globalVertexOffset = 0;
        var globalIndexOffset = 0;
        var clipOffset = drawData.DisplayPos;
        for (var listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            var drawList = drawData.CmdLists[listIndex];
            for (var commandIndex = 0; commandIndex < drawList.CmdBuffer.Size; commandIndex++)
            {
                var command = drawList.CmdBuffer[commandIndex];
                if (command.UserCallback != 0)
                    continue;

                var clip = command.ClipRect;
                var left = Math.Max(0, (int)(clip.X - clipOffset.X));
                var top = Math.Max(0, (int)(clip.Y - clipOffset.Y));
                var right = Math.Min((int)drawData.DisplaySize.X, (int)(clip.Z - clipOffset.X));
                var bottom = Math.Min((int)drawData.DisplaySize.Y, (int)(clip.W - clipOffset.Y));
                if (right <= left || bottom <= top)
                    continue;

                _commandList.RSSetScissorRects(new RawRect(left, top, right, bottom));
                var textureSlot = command.TextureId == 0 ? _fontSrvSlot : checked((int)command.TextureId);
                _commandList.SetGraphicsRootDescriptorTable(
                    ImGuiShaderLayout.TextureRootParameter,
                    _srvHeapGpuStart + textureSlot * _srvDescriptorSize);
                _commandList.DrawIndexedInstanced(
                    command.ElemCount,
                    1,
                    (uint)(globalIndexOffset + command.IdxOffset),
                    globalVertexOffset + (int)command.VtxOffset,
                    0);
            }

            globalIndexOffset += drawList.IdxBuffer.Size;
            globalVertexOffset += drawList.VtxBuffer.Size;
        }
    }

    private void ConfigureFonts()
    {
        var atlas = ImGuiNET.ImGui.GetIO().Fonts;
        BodyFont = AddFontOrDefault(atlas, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "consola.ttf"), 17.0f);
        TitleFont = AddFontOrDefault(atlas, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguisb.ttf"), 20.0f);
    }

    private static ImFontPtr AddFontOrDefault(ImFontAtlasPtr atlas, string path, float size) =>
        File.Exists(path) ? atlas.AddFontFromFileTTF(path, size) : atlas.AddFontDefault();

    private static void ConfigureStyle()
    {
        ImGuiNET.ImGui.StyleColorsDark();
        var style = ImGuiNET.ImGui.GetStyle();
        style.WindowRounding = 4.0f;
        style.FrameRounding = 3.0f;
        style.GrabRounding = 3.0f;
        style.WindowPadding = new Vector2(10.0f, 10.0f);
        style.FramePadding = new Vector2(7.0f, 4.0f);
        style.ItemSpacing = new Vector2(8.0f, 6.0f);
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.035f, 0.055f, 0.07f, 0.96f);
        style.Colors[(int)ImGuiCol.Header] = new Vector4(0.15f, 0.33f, 0.42f, 0.85f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.22f, 0.48f, 0.59f, 0.9f);
        style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.25f, 0.86f, 0.95f, 1.0f);
    }

    private void AddKeyboardEvents(ImGuiIOPtr io)
    {
        io.AddKeyEvent(ImGuiKey.ModCtrl, _input.IsDown(VirtualKey.Control));
        io.AddKeyEvent(ImGuiKey.ModShift, _input.IsDown(VirtualKey.Shift));
        io.AddKeyEvent(ImGuiKey.Tab, _input.IsDown(VirtualKey.Tab));
        io.AddKeyEvent(ImGuiKey.LeftArrow, _input.IsDown(VirtualKey.Left));
        io.AddKeyEvent(ImGuiKey.RightArrow, _input.IsDown(VirtualKey.Right));
        io.AddKeyEvent(ImGuiKey.UpArrow, _input.IsDown(VirtualKey.Up));
        io.AddKeyEvent(ImGuiKey.DownArrow, _input.IsDown(VirtualKey.Down));
        io.AddKeyEvent(ImGuiKey.Escape, _input.IsDown(VirtualKey.Escape));
    }

    public void Dispose()
    {
        if (_frameBegun)
            DiscardFrame();
        DisposePipeline();
        _fontTexture?.Dispose();
        _fontTexture = null;
        foreach (var frame in _frames)
            frame.Dispose();
        ImGuiNET.ImGui.DestroyContext(_context);
    }

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
