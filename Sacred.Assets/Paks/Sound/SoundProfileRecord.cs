namespace Sacred.Assets.Paks.Sound;

public sealed record SoundProfileRecord(
    uint ProfileId,
    string Name,
    IReadOnlyList<ushort> SoundIds);
