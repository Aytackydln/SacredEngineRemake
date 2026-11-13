using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold interpreter journal, stride 0x174 at +0x424. Quest entry
/// 0x46C160 builds this record; wire/bank serialization omits +0x0C..+0x0F.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptJournalLayout
{
    public const int SerializedSize = 372;
    public const int KindOffset = 0;
    public const int StateOffset = 4;
    public const int QuestNumberOffset = 8;
    public const int RuntimeFlagsOffset = 12;
    public const int RuntimeFlagsSize = 4;
    public const int CompassBlockOffset = 16;
    public const int CompassBlockWordCount = 5;
    public const uint ObjectTargetMarker = uint.MaxValue;
    public const uint ResourceTargetMarker = 0xEEEEEEEE;
    public const uint DefaultTargetRadius = 1200;
    public const string ObjectRadiusPrefix = "rad:";
    /// <summary>QuestKompassPos input X/Y (0x49A929..0x49A936); sentinels -1
    /// and EEEEEEEE select object/resource targets in the original refresh helper.</summary>
    public const int TargetXOffset = 16;
    public const int TargetYOffset = 20;
    /// <summary>With ObjectTargetMarker / ResourceTargetMarker in TargetX,
    /// TargetY instead holds the object reference / unresolved resource identity.
    /// Opcode 64 writes the same five-word shape to pending scratch when no
    /// journal exists, preserving both resolved-coordinate words.</summary>
    public const int ObjectOrResourceOffset = TargetYOffset;
    public const int ResolvedTileXOffset = 24;
    public const int ResolvedTileYOffset = 28;
    public const int TargetRadiusOffset = 32;
    public const int TitleResourceOffset = 36;
    public const int TextResourcesOffset = 40;
    public const int TextResourceCount = 10;
    /// <summary>Quest entry carries this word from the local player's previous
    /// main journal index, including when appending an other-quest entry.</summary>
    public const int Native16CWordOffset = 364;
    [FieldOffset(KindOffset)] public readonly uint Kind;
    [FieldOffset(StateOffset)] public readonly uint State;
    [FieldOffset(QuestNumberOffset)] public readonly int QuestNumber;
    [FieldOffset(RuntimeFlagsOffset)] public readonly uint RuntimeFlags;
    [FieldOffset(TargetXOffset)] public readonly uint TargetX;
    [FieldOffset(TargetYOffset)] public readonly uint TargetY;
    [FieldOffset(ResolvedTileXOffset)] public readonly uint ResolvedTileX;
    [FieldOffset(ResolvedTileYOffset)] public readonly uint ResolvedTileY;
    [FieldOffset(TargetRadiusOffset)] public readonly uint TargetRadius;
    [FieldOffset(Native16CWordOffset)] public readonly uint Native16CWord;
}
