using System.Numerics;
using Sacred.Assets.Executable;
using Sacred.Core.UI;

namespace Sacred.UI.Textures;

/// <summary>The native loader's crop, anchor and UV calculations, retaining the original table record.</summary>
public sealed class UiTextureRegion
{
    public SacredExecutableUiTextureDefinitionLayout Authored { get; }
    public string TextureName { get; }
    public Vector2 Size { get; }
    public Vector2 Anchor { get; }
    public Vector2 UvMinimum { get; }
    public Vector2 UvMaximum { get; }

    public UiTextureRegion(SacredExecutableUiTextureDefinitionLayout record)
    {
        Authored = record;
        TextureName = SacredUiTextureTable.GetTextureName(record);
        Size = new((int)(record.U1 + 1 - record.U0), (int)(record.V1 + 1 - record.V0));
        Anchor = record.Type == SacredExecutableUiAnchorKind.Button
            ? new((int)Size.X / 2, (int)Size.Y / 2)
            : new(record.AnchorX1 == 0 ? 0 : (int)(record.AnchorX1 - record.U0),
                record.AnchorY1 == 0 ? 0 : (int)(record.AnchorY1 - record.V0));
        UvMinimum = new(record.U0 / 256, record.V0 / 256);
        UvMaximum = new((record.U1 + 1) / 256 + 1f / 512, (record.V1 + 1) / 256 + 1f / 512);
    }
}
