namespace Sacred.Assets.Paks.Models;

/// <summary>Original Gold 0x4117B0 query against Models.tmp. Does not load a
/// mesh or choose a fallback animation. Loose resources use the original pak directory.</summary>
public sealed class ModelsMotionAvailability
{
    private readonly ModelsMetadataTable _metadata;
    private readonly Func<string, bool> _looseExists;
    private ModelsMotionAvailability(byte[] metadata, Func<string, bool> looseExists)
        => (_metadata, _looseExists) = (ModelsMetadataTable.Parse(metadata), looseExists);
    public bool HasMotion(string modelName, int slot) => _metadata.HasNativeMotion(modelName, slot, _looseExists);
    public static ModelsMotionAvailability Read(byte[] metadata, Func<string, bool> looseExists) => new(metadata, looseExists);
    public static ModelsMotionAvailability Load(string installation) => new(File.ReadAllBytes(Path.Combine(installation, "pak/Models.tmp")),
        name => File.Exists(Path.Combine(installation, "pak", name)) || Directory.Exists(Path.Combine(installation, "pak", name)));
}
