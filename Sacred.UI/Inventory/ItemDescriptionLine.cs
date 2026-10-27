namespace Sacred.UI.Inventory;

/// <summary>One localized line in the native item description, independent of its rendering host.</summary>
public sealed record ItemDescriptionLine(string Text, uint Color)
{
    public bool IsHeading { get; init; }
    public int? IconSelector { get; init; }
}
