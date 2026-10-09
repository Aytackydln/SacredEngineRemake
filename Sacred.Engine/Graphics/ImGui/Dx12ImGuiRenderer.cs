using System;
using System.IO;
using System.Numerics;
using ImGuiNET;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Platform;
using Sacred.Shaders;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Feeds engine input to Dear ImGui and records its draw data into the active DX12 frame.</summary>
internal sealed unsafe partial class Dx12ImGuiRenderer : IDisposable
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
    private bool _backgroundRecorded;
    private nint _backgroundDrawList;

    public bool IsFrameBegun => _frameBegun;

    public Dx12ImGuiRenderer(
        ID3D12Device device,
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader textureUploader,
        ID3D12DescriptorHeap srvHeap,
        int srvDescriptorSize,
        int fontSrvSlot,
        int frameCount,
        InputState input,
        string gameDirectory)
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
        ConfigureFonts(gameDirectory);

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
    public ImFontPtr MapFont { get; private set; }
    public ImFontPtr EscapeMenuFont { get; private set; }

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
        if (!_frameBegun && !_backgroundRecorded)
            return;

        var drawData = _backgroundRecorded ? ImGuiNET.ImGui.GetDrawData() : PrepareDrawData(frame);
        if (HasDrawData(drawData))
            RecordDrawData(drawData, _frames[frame.Index], uiPaperWhiteNits, false, _backgroundRecorded);
        _backgroundRecorded = false;
        _input.SetUiCapture(ImGuiNET.ImGui.GetIO().WantCaptureMouse, ImGuiNET.ImGui.GetIO().WantCaptureKeyboard);
    }

    /// <summary>Draws map lettering before native overlays; Record completes the controls afterward.</summary>
    public void RecordBackground(Dx12FrameContext frame, float uiPaperWhiteNits)
    {
        if (!_frameBegun)
            return;

        var drawData = PrepareDrawData(frame);
        if (HasDrawData(drawData))
            RecordDrawData(drawData, _frames[frame.Index], uiPaperWhiteNits, true, false);
        _backgroundRecorded = true;
    }

    private static bool HasDrawData(ImDrawDataPtr drawData) =>
        drawData.CmdListsCount > 0 && drawData.DisplaySize.X > 0.0f && drawData.DisplaySize.Y > 0.0f;

    private ImDrawDataPtr PrepareDrawData(Dx12FrameContext frame)
    {
        EnsureFontTexture(frame);
        ImGuiNET.ImGui.SetCurrentContext(_context);
        _backgroundDrawList = (nint)ImGuiNET.ImGui.GetBackgroundDrawList(ImGuiNET.ImGui.GetMainViewport()).NativePtr;
        ImGuiNET.ImGui.Render();
        _frameBegun = false;

        var drawData = ImGuiNET.ImGui.GetDrawData();
        if (!HasDrawData(drawData))
            return drawData;

        var resources = _frames[frame.Index];
        resources.EnsureCapacity(drawData.TotalVtxCount * VertexStride, drawData.TotalIdxCount * IndexStride);
        CopyDrawData(drawData, resources);
        return drawData;
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

    private void ConfigureFonts(string gameDirectory)
    {
        var atlas = ImGuiNET.ImGui.GetIO().Fonts;
        BodyFont = AddFontOrDefault(atlas, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "consola.ttf"), 17.0f);
        TitleFont = AddFontOrDefault(atlas, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguisb.ttf"), 20.0f);
        // Gold's map selects font slot 3: Carolingia. Use the installed game's face.
        var mapFontPath = Path.Combine(gameDirectory, "font", "CAROLING.TTF");
        MapFont = AddFontOrDefault(atlas, mapFontPath, 32.0f);
        // cUI_EscMenu selects native font slot 4, Carolingia at 20 pixels.
        EscapeMenuFont = AddFontOrDefault(atlas, mapFontPath, 20.0f);
        EngineLog.WriteLine($"World map font loaded: {mapFontPath}.");
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

}
