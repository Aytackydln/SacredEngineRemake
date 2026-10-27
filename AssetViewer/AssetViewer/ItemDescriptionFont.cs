using System;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace AssetViewer.AssetViewer;

/// <summary>Registers the installed game's font without installing it into Windows.</summary>
internal sealed class ItemDescriptionFont : FontCollectionBase
{
    public override Uri Key { get; } = new("fonts:sacred-item/" + Guid.NewGuid().ToString("N"));
    public FontFamily Family { get; }

    public ItemDescriptionFont(string gameDirectory)
    {
        using var stream = File.OpenRead(Path.Combine(gameDirectory, "font", "AntiquaSSK.TTF"));
        if (!TryAddGlyphTypeface(stream, out var face)) throw new InvalidDataException("Cannot load the game's item font.");
        FontManager.Current.AddFontCollection(this);
        Family = new FontFamily(Key + "#" + face.FamilyName);
    }

    public void Unregister() => FontManager.Current.RemoveFontCollection(Key);
}
