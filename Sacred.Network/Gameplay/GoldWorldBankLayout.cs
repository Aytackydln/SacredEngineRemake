namespace Sacred.Network.Gameplay;

/// <summary>Host state-bank stream layouts, verified at Gold GameServer 0x4CF6B0..0x4D144D.</summary>
public static class GoldWorldBankLayout
{
    public const ushort Bank1Opcode = 0x1B1;
    public const ushort Bank2Opcode = 0x1B2;
    public const ushort Bank3Opcode = 0x1B3;
    public const ushort Bank4Opcode = 0x1B4;
    public const int HeaderSize = 40;
    public const int HeaderWordCount = 10;
    /// <summary>Shared bank 2/3 count; use GetSectionCount for an arbitrary bank.</summary>
    public const int SectionCount = 7;
    public const int NameSize = 128;
    public const int WordSize = 4;
    public const int SectorRecordSize = 76;
    public const int NestedVectorsPrefixSize = 88;
    public const int RecordVectorsPrefixSize = 24;
    public const int NestedRecordFixedSize = 24;
    public const int RecordVectorElementSize = 44;

    public static int GetSectionCount(ushort opcode) => opcode switch
    {
        Bank1Opcode => 10, Bank2Opcode or Bank3Opcode => SectionCount, Bank4Opcode => 9,
        _ => throw new ArgumentOutOfRangeException(nameof(opcode))
    };

    /// <summary>Bank 1 uses all ten words; bank 4 uses nine. Banks 2/3 use seven.</summary>
    public static GoldWorldBankSectionKind GetSectionKind(ushort opcode, int index)
    {
        RequireSection(opcode, index);
        if (opcode == Bank1Opcode) return index is 1 or 5 ? GoldWorldBankSectionKind.KeyedRecords : GoldWorldBankSectionKind.FixedRecords;
        if (opcode == Bank4Opcode) return index switch
        {
            1 => GoldWorldBankSectionKind.NestedVectors,
            5 => GoldWorldBankSectionKind.RecordAndWordVectors,
            _ => GoldWorldBankSectionKind.FixedRecords
        };
        if (index == 0) return opcode == Bank2Opcode ? GoldWorldBankSectionKind.NamedWords : GoldWorldBankSectionKind.KeyedWords;
        return opcode == Bank2Opcode && index == 2 ? GoldWorldBankSectionKind.SectorRecords : GoldWorldBankSectionKind.FixedRecords;
    }

    public static int GetElementSize(ushort opcode, int index)
    {
        RequireSection(opcode, index);
        if (opcode == Bank1Opcode) return index switch
        {
            0 => 368, 1 => 8, 2 => 16, 3 => GoldScriptVariableLayout.RecordSize, 4 => 16, 5 => 32, 6 => 20, 7 or 8 => 76, 9 => 12,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
        if (opcode == Bank4Opcode) return index switch
        {
            0 => 80, 1 or 5 or 8 => 1, 2 => 24, 3 or 7 => 8, 4 => 88, 6 => 188,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
        return opcode == Bank2Opcode ? index switch
        {
            0 => WordSize, 1 => 128, 2 => SectorRecordSize, 3 => 68, 4 => 72, 5 => 100, 6 => 60,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        } : index switch
        {
            0 => WordSize, 1 => 292, 2 => 8, 3 => 88, 4 => GoldSectorObjectRestoreLayout.Size, 5 => 88, 6 => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    /// <summary>Server 0x4D22E7 and client 0x7E6CA3 transfer only runtime +0x120
    /// of each bank-3 section-1 record. Host uploads carry the full 292-byte record.</summary>
    public static int GetServerElementSize(ushort opcode, int index) =>
        opcode == Bank3Opcode && index == 1 ? WordSize : GetElementSize(opcode, index);

    private static void RequireSection(ushort opcode, int index)
    {
        if ((uint)index >= GetSectionCount(opcode)) throw new ArgumentOutOfRangeException(nameof(index));
    }
}

public enum GoldWorldBankSectionKind
{
    FixedRecords,
    NamedWords,
    KeyedWords,
    SectorRecords,
    NestedVectors,
    RecordAndWordVectors,
    KeyedRecords
}
