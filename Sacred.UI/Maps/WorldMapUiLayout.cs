using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.UI;
using Sacred.UI.Hud;
using Sacred.UI.Textures;

namespace Sacred.UI.Maps;

/// <summary>Native megamap frame and taskbar, independent of map zoom and world render resolution.</summary>
public sealed class WorldMapUiLayout
{
    public bool Gold { get; }
    public UiTextureRegion LeftPanel { get; }
    public UiTextureRegion RightPanel { get; }
    public Vector2 LeftPanelPosition { get; }
    public Vector2 RightPanelPosition { get; }
    public IReadOnlyList<WorldMapUiButtonDefinition> Buttons { get; }
    public UiDialogFrame? Frame { get; }
    public static HudRectangle FrameInset => new(new(48, 48), new(928, 672));

    public WorldMapUiLayout(IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> table, bool gold)
    {
        Gold = gold;
        var offset = gold ? 8 : 0;
        // Native UI_MAP_TBL/TBR, UI_MAP_PIN_UP/DN and UI_MAP_QUIT_UP/DN selectors.
        LeftPanel = new(table[220 + offset]);
        RightPanel = new(table[221 + offset]);
        var baseline = gold ? 750 : 768;
        LeftPanelPosition = new(512 - LeftPanel.Size.X, baseline - LeftPanel.Size.Y);
        RightPanelPosition = new(512, baseline - RightPanel.Size.Y);
        var buttonY = gold ? 685 : 703;
        Buttons = new WorldMapUiButtonDefinition[]
        {
            new(WorldMapUiButton.Waypoint, new(table[222 + offset]), new(table[223 + offset]), new(432, buttonY), false),
            new(WorldMapUiButton.Close, new(table[224 + offset]), new(table[225 + offset]), new(558, buttonY), true)
        };
        if (gold) Frame = new(table, true);
    }

    public static float GetScale(Vector2 viewport) => BottomHudLayout.GetScale(viewport);
    public static Vector2 ToScreen(Vector2 authored, Vector2 viewport) =>
        new WorldMapUiViewport(viewport).ControlsToScreen(authored);

    public HudRectangle GetMapBounds(Vector2 viewport) => Gold
        ? new WorldMapUiViewport(viewport).MapBounds
        : new(Vector2.Zero, viewport);

    public static HudRectangle GetButtonBounds(WorldMapUiButtonDefinition button, Vector2 viewport) =>
        new(ToScreen(button.Position, viewport), button.Up.Size * GetScale(viewport));

    public WorldMapUiButton? HitTest(Vector2 point, Vector2 viewport)
    {
        foreach (var button in Buttons)
            if (GetButtonBounds(button, viewport).Contains(point)) return button.Button;
        return null;
    }

    public bool ContainsPointer(Vector2 point, Vector2 viewport) =>
        (Gold && new WorldMapUiViewport(viewport).ContainsFrame(point)) ||
        new HudRectangle(ToScreen(LeftPanelPosition, viewport), LeftPanel.Size * GetScale(viewport)).Contains(point) ||
        new HudRectangle(ToScreen(RightPanelPosition, viewport), RightPanel.Size * GetScale(viewport)).Contains(point);
}
