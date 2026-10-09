using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.UI;
using Sacred.UI.Textures;

namespace Sacred.UI.Hud;

/// <summary>cUI_Taskbar2's controls and starting quick slots in its original 1024x768 authoring space.</summary>
public sealed class BottomHudLayout
{
    public const float CanvasHeight = 768;
    public static Vector2 DialCenter => new(513, 710.5f);
    public const float DialRadius = 27.5f;
    public UiTextureRegion Panel { get; }
    public Vector2 PanelPosition { get; }
    public IReadOnlyList<BottomHudButton> Buttons { get; }
    public IReadOnlyList<BottomHudDecoration> Decorations { get; }
    public BottomHudQuickSlotLayout QuickSlots { get; }

    public BottomHudLayout(IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> table)
    {
        // These are native eUITextureDef selectors, never Texture.pak entry IDs.
        Panel = new(table[11]); // UI_BASE_CENTER
        PanelPosition = new(512 - (int)Panel.Size.X / 2, 756 - Panel.Anchor.Y);
        QuickSlots = new(table);
        Buttons = new BottomHudButton[]
        {
            new(HudButton.Inventory, new(table[87]), new(table[88]), new(399, 705)),
            new(HudButton.Options, new(table[89]), new(table[90]), new(434, 729)),
            new(HudButton.Map, new(table[91]), new(table[92]), new(558, 729)),
            new(HudButton.QuestBook, new(table[93]), new(table[94]), new(594, 705)),
            new(HudButton.CollectItems, new(table[134]), new(table[135]), new(496, 743))
        };
        Decorations = new BottomHudDecoration[]
        {
            new(new(table[39]), new(424, 690)),
            new(new(table[40]), new(586, 690))
        };
    }

    public static float GetScale(Vector2 viewport) => MathF.Min(viewport.X / 1024, viewport.Y / CanvasHeight);

    // Preserve the native proportions while anchoring the center HUD to the output's bottom edge.
    public static Vector2 ToScreen(Vector2 authored, Vector2 viewport) =>
        new Vector2(viewport.X / 2, viewport.Y) +
        (authored - new Vector2(512, CanvasHeight)) * GetScale(viewport);

    public HudRectangle GetBounds(Vector2 viewport) =>
        new(ToScreen(PanelPosition, viewport), Panel.Size * GetScale(viewport));

    public bool ContainsPointer(Vector2 point, Vector2 viewport) =>
        GetBounds(viewport).Contains(point) || QuickSlots.ContainsPointer(point, viewport) ||
        HitTest(point, viewport).HasValue;

    public HudButton? HitTest(Vector2 point, Vector2 viewport)
    {
        foreach (var button in Buttons)
            if (GetButtonBounds(button, viewport).Contains(point)) return button.Button;
        return null;
    }

    public static HudRectangle GetButtonBounds(BottomHudButton button, Vector2 viewport) =>
        new(ToScreen(button.Position, viewport), button.Up.Size * GetScale(viewport));
}

public sealed record BottomHudButton(HudButton Button, UiTextureRegion Up, UiTextureRegion Down, Vector2 Position);
public sealed record BottomHudDecoration(UiTextureRegion Region, Vector2 Position);
