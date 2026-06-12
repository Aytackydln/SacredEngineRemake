namespace Sacred.ItemViewer.Avalonia.ItemViewer;

public enum ItemPreviewRotationMode
{
    LegacyCurrent,
    RawXyz,
    DirectYawPitchRoll,
    GrnMatrix
}

public enum ItemPreviewPivotMode
{
    BoundsCenter,
    ModelOrigin,
    BoundsBottomCenter,
    BoundsTopCenter,
    BoundsCenterGround
}
