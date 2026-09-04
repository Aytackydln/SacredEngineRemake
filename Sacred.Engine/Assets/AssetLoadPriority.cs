namespace Sacred.Engine.Assets;

/// <summary>Orders background asset work without ever making render code wait for it.</summary>
internal enum AssetLoadPriority
{
    Critical,
    Visible,
    Background
}
