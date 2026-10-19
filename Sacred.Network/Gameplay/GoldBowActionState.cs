using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>The creature's 64-byte action bank at native +0x154, copied by event 0x204.
/// Short banks are zero filled by the native receiver; bytes beyond 64 are ignored.</summary>
public sealed class GoldBowActionState
{
    public const int Size = GoldCombatLayout.MaximumAppliedActionStateBytes;
    public const int TargetReferenceOffset = 4;
    public const int TargetXOffset = 8;
    public const int TargetYOffset = 12;
    public const int ReleasedOffset = 0x24;
    public const int CombatArtOffset = 0x3A;
    private readonly byte[] _bytes = new byte[Size];
    public GoldBowActionState(ReadOnlySpan<byte> bytes) => bytes[..Math.Min(bytes.Length, Size)].CopyTo(_bytes);
    public uint TargetReference => Word(TargetReferenceOffset);
    public int TargetX => unchecked((int)Word(TargetXOffset));
    public int TargetY => unchecked((int)Word(TargetYOffset));
    public bool Released => _bytes[ReleasedOffset] != 0;
    public byte CombatArt => _bytes[CombatArtOffset];
    private uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(offset));
}
