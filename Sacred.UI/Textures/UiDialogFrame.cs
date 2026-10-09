using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Assets.Executable;
using Sacred.Core.UI;
using Sacred.UI.Hud;

namespace Sacred.UI.Textures;

/// <summary>Native cUI_Window2 frame type 1, including the reflected atlas crops.</summary>
public sealed class UiDialogFrame
{
    private readonly Dictionary<int, UiFramePiece> _pieces = [];
    public string TextureName { get; }

    public UiDialogFrame(IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> table, bool gold)
    {
        // Native UI_NET_FRAME2 selectors; these are not archive texture IDs.
        foreach (var selector in new[] { 301, 302, 303, 304, 309, 310, 311, 312 })
        {
            // Gold inserted nine definitions before this frame family.
            var r = table[selector + (gold ? 9 : 0)];
            var name = SacredUiTextureTable.GetTextureName(r);
            TextureName ??= name;
            if (name != TextureName) throw new InvalidOperationException("Dialog frame pieces must share their native atlas.");
            _pieces.Add(selector, new(new(MathF.Abs(r.U1 - r.U0) + 1, MathF.Abs(r.V1 - r.V0) + 1),
                new(r.U0 / 256, r.V0 / 256), new((r.U1 + 1) / 256, (r.V1 + 1) / 256)));
        }
        TextureName ??= string.Empty;
    }

    public IEnumerable<UiFrameQuad> GetQuads() => GetQuads(new(new(20, 20), new(224, 160)));

    public IEnumerable<UiFrameQuad> GetQuads(HudRectangle inset)
    {
        var left = inset.Position.X; var top = inset.Position.Y;
        var right = inset.Maximum.X; var bottom = inset.Maximum.Y;
        var tl = _pieces[309]; var tr = _pieces[310]; var bl = _pieces[311]; var br = _pieces[312];
        yield return new(tl, new(left - tl.Size.X, top - tl.Size.Y), tl.Size);
        yield return new(tr, new(right, top - tr.Size.Y), tr.Size);
        yield return new(bl, new(left - bl.Size.X, bottom), bl.Size);
        yield return new(br, new(right, bottom), br.Size);
        foreach (var quad in Tile(302, new(left, top - _pieces[302].Size.Y), inset.Size.X, true)) yield return quad;
        foreach (var quad in Tile(304, new(left, bottom + 12), inset.Size.X, true)) yield return quad;
        foreach (var quad in Tile(301, new(left - _pieces[301].Size.X, top), inset.Size.Y, false)) yield return quad;
        foreach (var quad in Tile(303, new(right + 15, top), inset.Size.Y, false)) yield return quad;
    }

    private IEnumerable<UiFrameQuad> Tile(int selector, Vector2 start, float extent, bool horizontal)
    {
        var piece = _pieces[selector];
        var step = horizontal ? piece.Size.X : piece.Size.Y;
        for (var offset = 0f; offset < extent; offset += step)
        {
            var length = MathF.Min(step, extent - offset);
            var size = horizontal ? new Vector2(length, piece.Size.Y) : new Vector2(piece.Size.X, length);
            var uv = Vector2.Lerp(piece.UvMinimum, piece.UvMaximum, length / step);
            var cropped = piece with { UvMaximum = horizontal ? new(uv.X, piece.UvMaximum.Y) : new(piece.UvMaximum.X, uv.Y) };
            yield return new(cropped, start + (horizontal ? new(offset, 0) : new Vector2(0, offset)), size);
        }
    }
}
