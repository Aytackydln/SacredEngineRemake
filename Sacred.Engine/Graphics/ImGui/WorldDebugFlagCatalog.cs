using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.Pak.Items;
using Sacred.Core.World;
using Sacred.Core.World.Pathing;
using Sacred.Core.World.Sector;

namespace Sacred.Engine.Graphics.ImGui;

internal readonly record struct WorldDebugFlagOption<T>(
    T Flag,
    string Label,
    Vector4 Colour)
    where T : struct, Enum;

internal readonly record struct WorldDebugByteFlagOption(
    byte Flag,
    string Label,
    Vector4 Colour);

/// <summary>Raw game-file flag bits available to the interactive world debugger.</summary>
internal static class WorldDebugFlagCatalog
{
    private static readonly Vector4[] Colours =
    [
        new(0.30f, 1.00f, 0.35f, 0.95f),
        new(0.25f, 0.65f, 1.00f, 0.95f),
        new(1.00f, 0.78f, 0.15f, 0.95f),
        new(1.00f, 0.25f, 0.70f, 0.95f),
        new(0.94f, 0.55f, 0.20f, 0.95f),
        new(0.66f, 0.48f, 1.00f, 0.95f),
        new(0.20f, 0.92f, 0.88f, 0.95f),
        new(1.00f, 1.00f, 1.00f, 0.95f),
    ];

    public static IReadOnlyList<WorldDebugFlagOption<WorldPathFlags>> PathFlags { get; } =
    [
        Option(WorldPathFlags.Indoor, "Indoor", 0),
        Option(WorldPathFlags.Town, "Town", 1),
        Option(WorldPathFlags.Trigger, "Trigger", 2),
        Option(WorldPathFlags.RuntimeBlocked, "Runtime blocked", 3),
        Option(WorldPathFlags.Byte10, "Byte10", 4),
        Option(WorldPathFlags.Byte20, "Byte20", 5),
        Option(WorldPathFlags.Byte40, "Byte40", 6),
        Option(WorldPathFlags.Byte80, "Byte80", 7),
    ];

    public static IReadOnlyList<WorldDebugFlagOption<WldxTileFlags>> TileFlags { get; } =
    [
        Option(WldxTileFlags.MovementBlockerA, "Movement blocker A", 0),
        Option(WldxTileFlags.MovementBlockerB, "Movement blocker B", 1),
        Option(WldxTileFlags.Entrance, "Entrance", 2),
    ];

    public static IReadOnlyList<WorldDebugFlagOption<WldxTerrainSurface>> SurfaceFlags { get; } =
    [
        Option(WldxTerrainSurface.Surface10, "Surface bit 0x10", 4),
        Option(WldxTerrainSurface.Surface20, "Surface bit 0x20", 5),
        Option(WldxTerrainSurface.Surface40, "Surface bit 0x40", 6),
        Option(WldxTerrainSurface.LiquidFamilyMarker, "Surface bit 0x80", 7),
    ];

    public static IReadOnlyList<WorldDebugFlagOption<SectorEnvironmentFlags>> SectorFlags { get; } =
    [
        Option(SectorEnvironmentFlags.Byte01, "Byte01", 0),
        Option(SectorEnvironmentFlags.Byte02, "Byte02", 1),
        Option(SectorEnvironmentFlags.Dungeon, "Dungeon", 2),
        Option(SectorEnvironmentFlags.Byte08, "Byte08", 3),
        Option(SectorEnvironmentFlags.NorthBoundary, "North boundary", 4),
        Option(SectorEnvironmentFlags.EastBoundary, "East boundary", 5),
        Option(SectorEnvironmentFlags.SouthBoundary, "South boundary", 6),
        Option(SectorEnvironmentFlags.WestBoundary, "West boundary", 7),
    ];

    public static IReadOnlyList<WorldDebugFlagOption<StaticObjectFlags>> StaticFlags { get; } =
        CreateStaticObjectOptions();

    public static IReadOnlyList<WorldDebugFlagOption<SacredItemGraphicFlags>> ItemGraphicFlags { get; } =
        CreateItemGraphicOptions();

    public static IReadOnlyList<WorldDebugByteFlagOption> ItemDescriptorByteFlags { get; } =
        CreateByteOptions();

    private static WorldDebugFlagOption<T> Option<T>(T flag, string label, int colourIndex)
        where T : struct, Enum =>
        new(flag, $"{label} ({Hex(flag)})", Colours[colourIndex % Colours.Length]);

    private static IReadOnlyList<WorldDebugFlagOption<StaticObjectFlags>> CreateStaticObjectOptions()
    {
        var options = new List<WorldDebugFlagOption<StaticObjectFlags>>(32);
        foreach (var flag in Enum.GetValues<StaticObjectFlags>())
        {
            var value = (uint)flag;
            if (value == 0 || !IsSingleBit(value))
                continue;

            var label = flag switch
            {
                StaticObjectFlags.AlternateSurface => "Alternate surface",
                StaticObjectFlags.RearLayerBackground => "Rear-layer background",
                StaticObjectFlags.NightOnly => "Night only",
                _ => flag.ToString(),
            };
            options.Add(Option(flag, label, BitIndex(value)));
        }

        return options;
    }

    private static IReadOnlyList<WorldDebugFlagOption<SacredItemGraphicFlags>> CreateItemGraphicOptions()
    {
        var options = new List<WorldDebugFlagOption<SacredItemGraphicFlags>>(12);
        foreach (var flag in Enum.GetValues<SacredItemGraphicFlags>())
        {
            var value = (ushort)flag;
            if (value == 0 || !IsSingleBit(value))
                continue;

            var label = flag switch
            {
                SacredItemGraphicFlags.CastsStaticShadow => "Casts static shadow",
                SacredItemGraphicFlags.LightEmitting => "Light emitting",
                SacredItemGraphicFlags.MultitextureScroll => "Multitexture scroll",
                SacredItemGraphicFlags.VerticalTextureScroll => "Vertical texture scroll",
                SacredItemGraphicFlags.FrontLayer => "Front layer",
                _ => flag.ToString(),
            };
            options.Add(Option(flag, label, BitIndex(value)));
        }

        return options;
    }

    private static IReadOnlyList<WorldDebugByteFlagOption> CreateByteOptions()
    {
        var options = new WorldDebugByteFlagOption[8];
        for (var bit = 0; bit < options.Length; bit++)
        {
            var value = (byte)(1 << bit);
            options[bit] = new WorldDebugByteFlagOption(
                value,
                $"Bit 0x{value:X2}",
                Colours[bit]);
        }

        return options;
    }

    private static bool IsSingleBit(uint value) => (value & (value - 1)) == 0;

    private static int BitIndex(uint value)
    {
        var index = 0;
        while ((value >>= 1) != 0)
            index++;
        return index;
    }

    private static string Hex<T>(T flag) where T : struct, Enum =>
        $"0x{Convert.ToUInt64(flag):X}";
}
