namespace Sacred.Granny.Native;

/// <summary>Mapped render-state slots exposed by the x64 Granny 1.2b compatibility ABI.</summary>
internal static class GrannyX64RenderingStateLayout
{
    // The legacy state occupied 0xCC bytes. The x64 ABI can extend its tail as
    // pointer-width fields are written, so callers provide a larger cleared buffer.
    public const int BufferSize = 0x200;

    // granny_rendering_state.LoadTransform and Transform. When LoadTransform
    // is set, the Direct3D renderer loads this matrix before the state's
    // vertex arrays. A following state with LoadTransform clear retains it.
    public const int LoadTransformOffset = 0x1C;
    public const int TransformPointerOffset = 0x20;

    public const int PositionCountOffset = 0x38;
    public const int PositionPointerOffset = 0x40;
    public const int TextureCoordinateCountOffset = 0x80;
    public const int TextureCoordinatePointerOffset = 0x88;
    public const int NormalCountOffset = 0x98;
    public const int NormalPointerOffset = 0xA0;
    public const int TriangleCountOffset = 0xB0;
    public const int TrianglePointerOffset = 0xB8;
}
