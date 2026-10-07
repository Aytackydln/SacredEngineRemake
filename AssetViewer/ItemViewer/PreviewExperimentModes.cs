using Raiqub.Generators.EnumUtilities;

namespace AssetViewer.ItemViewer;

[EnumGenerator]
public enum ItemPreviewRotationMode
{
    Auto,
    LegacyCurrent,
    RawXyz,
    DirectYawPitchRoll,
    GrnMatrix,
    None,
}

[EnumGenerator]
public enum ItemPreviewPivotMode
{
    BoundsCenter,
    ModelOrigin,
    BoundsBottomCenter,
    BoundsTopCenter,
    BoundsCenterGround,
    WholeModelBoundsCenter,
    WholeModelBoundsBottomCenter,
    WholeModelBoundsTopCenter,
    WholeRigCenter,
    WholeRigFeetCenter,
    WholeRigTopCenter,
    RootBone,
    SelectedBone
}
