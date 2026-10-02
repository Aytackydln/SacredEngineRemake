using System;
using Sacred.Core.World.Sector;
using Sacred.Engine.Rendering;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Terrain;

internal sealed record SectorCompositionRequest(
    TerrainSectorComposition Composition,
    int BaseSrvSlot,
    int LiquidCoverSrvSlot,
    bool ReplacesVisibleTexture,
    long Sequence);

internal sealed record SubmittedSectorComposition(
    SectorCoord Coord,
    TerrainSectorComposition Composition,
    Dx12ComposedSector? Composed,
    int BaseSrvSlot,
    int LiquidCoverSrvSlot,
    Exception? Error);

internal sealed class SectorTexture(
    TerrainSectorComposition composition,
    ID3D12Resource baseResource,
    ID3D12Resource liquidCoverResource,
    int baseSrvSlot,
    int liquidCoverSrvSlot)
{
    public TerrainSectorComposition Composition { get; } = composition;
    public ID3D12Resource BaseResource { get; } = baseResource;
    public ID3D12Resource LiquidCoverResource { get; } = liquidCoverResource;
    public int BaseSrvSlot { get; } = baseSrvSlot;
    public int LiquidCoverSrvSlot { get; } = liquidCoverSrvSlot;
    public long LastUsedSequence { get; set; }
}

internal readonly record struct SectorTextureView(
    int BaseSrvSlot,
    int LiquidCoverSrvSlot);
