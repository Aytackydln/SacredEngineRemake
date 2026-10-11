using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Sacred.Inventory.Actors;
using Sacred.UI.Inventory;

namespace AssetViewer.AssetViewer;

internal sealed class CharacterWeaponSlotsPane : UserControl, IDisposable
{
    public IReadOnlyList<EquipmentSlotControl> Slots { get; }

    public CharacterWeaponSlotsPane(CharacterSlotRow[] rows, EquipmentPanelAssets assets,
        EquipmentPanelArtwork artwork, EquipmentSlotPreviewRenderer previews)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Center };
        var controls = new List<EquipmentSlotControl>();
        foreach (var type in new[] { EquipmentSlotType.RightHand, EquipmentSlotType.LeftHand })
        {
            var row = rows.Single(r => r.Type == type);
            var column = new StackPanel { Spacing = 4 };
            column.Children.Add(new TextBlock { Text = type == EquipmentSlotType.RightHand ? "Main hand" : "Offhand",
                HorizontalAlignment = HorizontalAlignment.Center });
            var slot = new EquipmentSlotControl(row, assets.WeaponSlotArt, artwork, previews, assets.WeaponSlotBackground);
            column.Children.Add(slot); controls.Add(slot); panel.Children.Add(column);
        }
        Slots = controls;
        Content = panel;
    }

    public void Dispose() { foreach (var slot in Slots) slot.Dispose(); }
}
