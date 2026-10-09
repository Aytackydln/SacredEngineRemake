using System;
using System.IO;
using System.Threading.Tasks;
using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Texture;
using Sacred.UI.Textures;

namespace Sacred.UI.Menus;

public sealed class EscapeMenuAssets
{
    public EscapeMenuLayout Layout { get; }
    public UiDialogFrame Frame { get; }
    public TextureAsset Texture { get; }

    private EscapeMenuAssets(EscapeMenuLayout layout, UiDialogFrame frame, TextureAsset texture)
    { Layout = layout; Frame = frame; Texture = texture; }

    public static async Task<EscapeMenuAssets> LoadAsync(string gameDirectory, Func<string, Task<TextureAsset>> load)
    {
        var path = Path.Combine(gameDirectory, "Sacred.exe");
        if (!File.Exists(path)) path = Path.Combine(gameDirectory, "pureHD.exe");
        var image = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        var table = SacredUiTextureTable.Read(image);
        var hasExport = image.AsSpan().IndexOf("UI_ESC_EXPORT\0"u8) >= 0;
        var frame = new UiDialogFrame(table, hasExport);
        var texture = await load(frame.TextureName).ConfigureAwait(false);
        return new(new(hasExport), frame, texture);
    }
}
