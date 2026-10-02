using System;
using System.Collections.Generic;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Graphics.Uploads;
using Sacred.Engine.Rendering;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Terrain;

/// <summary>Stages sector sources on the bounded COPY queue before any raster commands are submitted.</summary>
internal sealed class Dx12SectorSourceTextureCache(Dx12TextureUploadWorker uploads) : IDisposable
{
    private const int MaximumTileSheets = 4096;
    private const int MaximumRequestsPerOpportunity = 8;
    private readonly Dictionary<string, Entry> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<StaticSpriteAsset, Entry> _sprites = new(ReferenceEqualityComparer.Instance);
    private int _requestsRemaining;

    public bool Prepare(TerrainSectorComposition composition)
    {
        _requestsRemaining = MaximumRequestsPerOpportunity;
        var ready = true;
        foreach (var tile in composition.BaseTiles) ready &= Prepare(tile);
        foreach (var tile in composition.CoverTiles) ready &= Prepare(tile);
        foreach (var sprite in composition.EmbeddedSprites)
            ready &= Prepare(_sprites, sprite.Sprite, $"sector-sprite:{sprite.Sprite.GroupId}",
                sprite.Sprite.AtlasWidth, sprite.Sprite.AtlasHeight, sprite.Sprite.Rgba);
        return ready;
    }

    private bool Prepare(TerrainCompositionTile tile)
    {
        var ready = Prepare(tile.Primary.Texture);
        if (tile.Secondary is { } secondary) ready &= Prepare(secondary.Texture);
        return ready;
    }

    private bool Prepare(TextureAsset texture)
    {
        if (!_tiles.ContainsKey(texture.Name) && _tiles.Count >= MaximumTileSheets)
            throw new InvalidOperationException($"The terrain tile-sheet cache exhausted its {MaximumTileSheets} textures.");
        return Prepare(_tiles, texture.Name, texture.Name, texture.Width, texture.Height, texture.Rgba8);
    }

    private bool Prepare<TKey>(Dictionary<TKey, Entry> entries, TKey key, string name,
        int width, int height, byte[] pixels) where TKey : notnull
    {
        if (!entries.TryGetValue(key, out var entry))
        {
            if (_requestsRemaining == 0 || !uploads.TryEnqueue(name, width, height, pixels, null, out var ticket))
                return false;
            _requestsRemaining--;
            entries.Add(key, entry = new Entry(ticket));
        }
        if (entry.Source is not null) return true;
        if (!entry.Ticket.TryTake(out var resource, out var error)) return false;
        if (error is not null || resource is null)
            throw new InvalidOperationException($"Sector source copy failed: {entry.Ticket.Name}.", error);
        entry.Source = new SourceTexture(resource);
        return true;
    }

    public SourceTexture Get(TextureAsset texture, ID3D12GraphicsCommandList commands) =>
        MakeReadable(_tiles[texture.Name], commands);

    public SourceTexture Get(StaticSpriteAsset sprite, ID3D12GraphicsCommandList commands) =>
        MakeReadable(_sprites[sprite], commands);

    private static SourceTexture MakeReadable(Entry entry, ID3D12GraphicsCommandList commands)
    {
        var source = entry.Source ?? throw new InvalidOperationException("Sector source is still awaiting its copy fence.");
        if (!entry.Readable)
        {
            Dx12TextureUploader.Transition(commands, source.Resource,
                ResourceStates.Common, ResourceStates.PixelShaderResource);
            entry.Readable = true;
        }
        return source;
    }

    // Recording failures do not execute any of the recorded source transitions.
    public void ResetRecordedTransitions()
    {
        foreach (var entry in _tiles.Values) entry.Readable = entry.Submitted;
        foreach (var entry in _sprites.Values) entry.Readable = entry.Submitted;
    }

    public void CommitRecordedTransitions()
    {
        foreach (var entry in _tiles.Values) entry.Submitted = entry.Readable;
        foreach (var entry in _sprites.Values) entry.Submitted = entry.Readable;
    }

    public void Dispose()
    {
        foreach (var entry in _tiles.Values) entry.Dispose();
        foreach (var entry in _sprites.Values) entry.Dispose();
        _tiles.Clear();
        _sprites.Clear();
    }

    private sealed class Entry(TextureUploadTicket ticket) : IDisposable
    {
        public TextureUploadTicket Ticket { get; } = ticket;
        public SourceTexture? Source { get; set; }
        public bool Readable { get; set; }
        public bool Submitted { get; set; }
        public void Dispose()
        {
            Ticket.Cancel();
            Source?.Resource.Dispose();
        }
    }
}
