namespace Sacred.Assets.Paks.Texture;

/// <summary>A texture table row identified by both its source archive and descriptor index.</summary>
public readonly record struct TexturePakEntry(string ArchiveName, uint EntryId, TexturePakRecord Record);
