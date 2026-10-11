using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Sacred.Inventory.Actors;
using Sacred.UI.Inventory;

namespace AssetViewer.AssetViewer;

internal sealed class CharacterArmorSlotsPane : UserControl, IDisposable
{
    public IReadOnlyList<EquipmentSlotControl> Slots { get; }

    public CharacterArmorSlotsPane(SacredCharacterClass characterClass, CharacterSlotRow[] rows,
        EquipmentPanelAssets assets, EquipmentPanelArtwork artwork, EquipmentSlotPreviewRenderer previews)
    {
        var layout = new EquipmentSlotsLayout(characterClass, assets.Definitions);
        var canvas = new Canvas { Width = 288, Height = 296 };
        var background = new Image { Source = artwork.Get(assets.Background), Width = 256, Height = 256,
            Stretch = Stretch.Fill, Opacity = 159d / 255, IsHitTestVisible = false };
        Put(canvas, background, 16, 20);
        var controls = new List<EquipmentSlotControl>();
        foreach (var definition in layout.Slots)
        {
            var row = rows.Single(r => r.Type == definition.Type && r.Occurrence == definition.Occurrence);
            var slot = new EquipmentSlotControl(row, definition.Art, artwork, previews);
            controls.Add(slot);
            Put(canvas, slot, 16 + definition.Position.X, 20 + definition.Position.Y);
        }
        foreach (var decoration in assets.Frame.Pieces)
            Put(canvas, new Image { Source = artwork.Get(decoration.Art), Width = decoration.Art.Size.X,
                Height = decoration.Art.Size.Y, Stretch = Stretch.Fill, IsHitTestVisible = false },
                16 + decoration.Position.X, 20 + decoration.Position.Y);
        Slots = controls;
        Content = canvas;
    }

    private static void Put(Canvas canvas, Control control, double x, double y)
    {
        Canvas.SetLeft(control, x); Canvas.SetTop(control, y); canvas.Children.Add(control);
    }

    public void Dispose() { foreach (var slot in Slots) slot.Dispose(); }
}
