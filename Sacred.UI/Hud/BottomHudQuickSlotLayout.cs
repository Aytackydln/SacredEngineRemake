using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Assets.Executable;
using Sacred.Core.UI;
using Sacred.UI.Inventory;
using Sacred.UI.Textures;

namespace Sacred.UI.Hud;

/// <summary>Native level-dependent hand/combat-art frames and their ornamental taskbar wings.</summary>
public sealed class BottomHudQuickSlotLayout
{
    public BottomHudDecoration HandSlot => HandSlots[0];
    public BottomHudDecoration HandBackground => HandBackgrounds[0];
    public IReadOnlyList<BottomHudDecoration> HandSlots { get; private set; } = [];
    public IReadOnlyList<BottomHudDecoration> HandBackgrounds { get; private set; } = [];
    public IReadOnlyList<BottomHudDecoration> CombatArtSlots { get; private set; } = [];
    public IReadOnlyList<BottomHudDecoration> CombatArtBackgrounds { get; private set; } = [];
    public IReadOnlyList<BottomHudDecoration> Wings { get; private set; } = [];
    public WeaponQuickSlotLayout Weapon { get; }
    private readonly UiTextureRegion _combatArtBackground;
    private readonly UiTextureRegion _leftEnd, _leftContinuation, _rightEnd, _rightContinuation;
    private (int Count, int Weapon, int Art) _configuration;

    public BottomHudQuickSlotLayout(IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> table)
    {
        Weapon = new(table);
        _combatArtBackground = BottomHudSlotBackgrounds.CombatArt(table);
        _leftEnd = new(table[5]);
        _leftContinuation = new(table[6 + Random.Shared.Next(2)]);
        _rightEnd = new(table[8]);
        _rightContinuation = new(table[9 + Random.Shared.Next(2)]);
        SetSlots(1, 0, 0);
    }

    public void SetSlots(int count, int selectedWeapon, int selectedCombatArt)
    {
        count = Math.Clamp(count, 1, SacredQuickSlotDefinitions.MaximumSlots);
        // Keep invalid received selections unselected, matching native equality tests.
        var configuration = (count, selectedWeapon, selectedCombatArt);
        if (_configuration == configuration) return;
        var hands = new BottomHudDecoration[count];
        var handBackgrounds = new BottomHudDecoration[count];
        var arts = new BottomHudDecoration[count];
        var artBackgrounds = new BottomHudDecoration[count];
        for (var index = 0; index < count; index++)
        {
            var handPosition = new Vector2(394 + 66 * (index - count), 691);
            var artPosition = new Vector2(640 + 66 * index, 691);
            hands[index] = new(index == selectedWeapon ? Weapon.Frame : Weapon.InactiveFrame, handPosition);
            handBackgrounds[index] = new(index == selectedWeapon ? Weapon.Empty : Weapon.InactiveBackground, handPosition);
            arts[index] = new(index == selectedCombatArt ? Weapon.Frame : Weapon.InactiveFrame, artPosition);
            artBackgrounds[index] = new(index == selectedCombatArt ? _combatArtBackground : Weapon.InactiveBackground, artPosition);
        }
        var wings = new List<BottomHudDecoration>();
        for (float x = 362 - 66 * count; x < 397;)
        {
            var region = x == 362 - 66 * count ? _leftEnd : _leftContinuation;
            wings.Add(Wing(region, x));
            x += region.Size.X;
        }
        for (float x = 560 + 66 * count; x > 547;)
        {
            var region = x == 560 + 66 * count ? _rightEnd : _rightContinuation;
            wings.Add(Wing(region, x));
            x -= region.Size.X;
        }
        HandSlots = hands;
        HandBackgrounds = handBackgrounds;
        CombatArtSlots = arts;
        CombatArtBackgrounds = artBackgrounds;
        Wings = wings;
        _configuration = configuration;
    }

    public bool ContainsPointer(Vector2 point, Vector2 viewport)
    {
        foreach (var slot in HandSlots)
            if (GetBounds(slot, viewport).Contains(point)) return true;
        foreach (var slot in CombatArtSlots)
            if (GetBounds(slot, viewport).Contains(point)) return true;
        foreach (var wing in Wings)
            if (GetBounds(wing, viewport).Contains(point)) return true;
        return false;
    }

    private static BottomHudDecoration Wing(UiTextureRegion region, float x) =>
        new(region, new(x, 756 - region.Anchor.Y));

    public static HudRectangle GetBounds(BottomHudDecoration piece, Vector2 viewport) =>
        new(BottomHudLayout.ToScreen(piece.Position, viewport),
            piece.Region.Size * BottomHudLayout.GetScale(viewport));
}
