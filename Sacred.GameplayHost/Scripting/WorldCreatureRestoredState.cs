using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Private creature import after the physical stage. Unresolved saved
/// equipment identities are retained separately until their actual objects are
/// restored through the session reference plan. No network replica is seeded.</summary>
public sealed class WorldCreatureRestoredState
{
    private readonly byte[] _prefix;
    private readonly SacredSavedCreature _source;
    public WorldCreatureRestoredState(SacredSavedCreature saved, IReadOnlyList<byte[]> arts)
    {
        _source = saved;
        _prefix = saved.CreaturePrefix.ToArray();
        CombatArts = arts.Select(row => row.ToArray()).ToArray();
        Combos = Enumerable.Range(0, saved.Values.Count38).Select(index => saved.Values.GetRecord38(index).ToArray()).ToArray();
        StatusModifiers = Enumerable.Range(0, saved.Values.Count28).Select(index => saved.Values.GetRecord28(index).ToArray()).ToArray();
        PendingEquipmentReferences = Enumerable.Range(0, SacredSavedCreatureLayout.EquipmentCount).Select(saved.GetEquipmentReference).ToArray();
        HeroAttachment = saved.HeroAttachment.ToArray();
    }
    public IReadOnlyList<byte[]> CombatArts { get; }
    public IReadOnlyList<byte[]> Combos { get; }
    public IReadOnlyList<byte[]> StatusModifiers { get; }
    public IReadOnlyList<uint> PendingEquipmentReferences { get; }
    public ReadOnlyMemory<byte> HeroAttachment { get; }
    public float AnimationSpeedMultiplier => BitConverter.UInt32BitsToSingle(Word(SacredSavedCreatureLayout.AnimationSpeedMultiplierOffset));
    public uint Word210 => Word(SacredSavedCreatureLayout.Block1F0Offset + 0x20);
    internal WorldCreatureRestoredState WithCombatArts(IReadOnlyList<byte[]> arts) => new(_source, arts);
    internal bool HasExtraLists => _source.Count9 != 0 || _source.Count68 != 0;
    internal uint Word(int fileOffset) => BinaryPrimitives.ReadUInt32LittleEndian(_prefix.AsSpan(fileOffset - SacredSavedCreatureLayout.MarkerOffset));
    internal ushort Short(int fileOffset) => BinaryPrimitives.ReadUInt16LittleEndian(_prefix.AsSpan(fileOffset - SacredSavedCreatureLayout.MarkerOffset));
}
