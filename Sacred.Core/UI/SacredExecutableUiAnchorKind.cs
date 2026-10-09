namespace Sacred.Core.UI;

/// <summary>Native eUIType values; the inspected Windows tables use None and Button.</summary>
public enum SacredExecutableUiAnchorKind : uint
{
    None = 0,
    LeftTop = 1,
    Top = 2,
    RightTop = 3,
    Right = 4,
    RightBottom = 5,
    Bottom = 6,
    LeftBottom = 7,
    Left = 8,
    Button = 9
}
