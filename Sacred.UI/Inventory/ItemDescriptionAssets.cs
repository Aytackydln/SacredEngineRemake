using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Texture;
using Sacred.UI.Textures;

namespace Sacred.UI.Inventory;

public sealed class ItemDescriptionAssets
{
    public ItemDescriptionFrame Frame { get; }
    public IReadOnlyDictionary<string, TextureAsset> Textures { get; }
    public ItemDescriptionEquipmentRules EquipmentRules { get; }
    public UiTextureRegion Socket { get; }
    public IReadOnlyDictionary<int, UiTextureRegion> Emblems { get; }

    private ItemDescriptionAssets(ItemDescriptionFrame frame, Dictionary<string, TextureAsset> textures,
        ItemDescriptionEquipmentRules rules, UiTextureRegion socket, Dictionary<int, UiTextureRegion> emblems)
    { Frame = frame; Textures = textures; EquipmentRules = rules; Socket = socket; Emblems = emblems; }

    public static async Task<ItemDescriptionAssets> LoadAsync(string gameDirectory, Func<string, Task<TextureAsset>> load)
    {
        var path = Path.Combine(gameDirectory, "Sacred.exe");
        if (!File.Exists(path)) path = Path.Combine(gameDirectory, "pureHD.exe");
        var definitions = await Task.Run(() => SacredUiTextureTable.Read(path)).ConfigureAwait(false);
        var frame = new ItemDescriptionFrame(definitions);
        var rules = await Task.Run(() => SacredItemDescriptionDefinitions.LoadTwoHandedBases(gameDirectory)).ConfigureAwait(false);
        var socket = new UiTextureRegion(definitions[171]);
        var emblems = Enumerable.Range(414, 5).ToDictionary(selector => selector, selector => new UiTextureRegion(definitions[selector]));
        var textures = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in frame.GetQuads(350, 200).Select(piece => piece.TextureName)
                     .Append(socket.TextureName).Concat(emblems.Values.Select(region => region.TextureName)).Distinct(StringComparer.OrdinalIgnoreCase))
            textures.Add(name, await load(name).ConfigureAwait(false));
        return new(frame, textures, new(rules), socket, emblems);
    }
}
