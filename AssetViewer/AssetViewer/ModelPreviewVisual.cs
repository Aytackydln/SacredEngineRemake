using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;

namespace AssetViewer.AssetViewer;

internal readonly record struct ModelPreviewVisual(
    ItemsPakEntry Item, SacredEquipment? Equipment = null, string? RigidAttachBoneName = null);
