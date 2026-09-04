using System;
using Sacred.Core.World.Sector;
using Sacred.Engine.Rendering;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Terrain;

internal sealed record SectorCompositionRequest(
    TerrainSectorComposition Composition,
    int BaseSrvSlot,
    int LiquidCoverSrvSlot,
    int StairsDebugSrvSlot,
    int BlockedAreaDebugSrvSlot,
    int TerrainTopologyDebugSrvSlot);

internal sealed record SubmittedSectorComposition(
    SectorCoord Coord,
    TerrainSectorComposition Composition,
    Dx12ComposedSector? Composed,
    int BaseSrvSlot,
    int LiquidCoverSrvSlot,
    int StairsDebugSrvSlot,
    int BlockedAreaDebugSrvSlot,
    int TerrainTopologyDebugSrvSlot,
    Exception? Error);

internal sealed class SectorTexture(
    TerrainSectorComposition composition,
    ID3D12Resource baseResource,
    ID3D12Resource liquidCoverResource,
    ID3D12Resource stairsDebugResource,
    ID3D12Resource blockedAreaDebugResource,
    ID3D12Resource terrainTopologyDebugResource,
    int baseSrvSlot,
    int liquidCoverSrvSlot,
    int stairsDebugSrvSlot,
    int blockedAreaDebugSrvSlot,
    int terrainTopologyDebugSrvSlot)
{
    public TerrainSectorComposition Composition { get; } = composition;
    public ID3D12Resource BaseResource { get; } = baseResource;
    public ID3D12Resource LiquidCoverResource { get; } = liquidCoverResource;
    public ID3D12Resource StairsDebugResource { get; } = stairsDebugResource;
    public ID3D12Resource BlockedAreaDebugResource { get; } = blockedAreaDebugResource;
    public ID3D12Resource TerrainTopologyDebugResource { get; } = terrainTopologyDebugResource;
    public int BaseSrvSlot { get; } = baseSrvSlot;
    public int LiquidCoverSrvSlot { get; } = liquidCoverSrvSlot;
    public int StairsDebugSrvSlot { get; } = stairsDebugSrvSlot;
    public int BlockedAreaDebugSrvSlot { get; } = blockedAreaDebugSrvSlot;
    public int TerrainTopologyDebugSrvSlot { get; } = terrainTopologyDebugSrvSlot;
}

internal readonly record struct SectorTextureView(
    int BaseSrvSlot,
    int LiquidCoverSrvSlot,
    int StairsDebugSrvSlot,
    int BlockedAreaDebugSrvSlot,
    int TerrainTopologyDebugSrvSlot);
