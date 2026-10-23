using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Immutable native world-bank stream. Server snapshots and host uploads have distinct
/// bank-3 section-1 layouts; retaining bytes does not execute their world effects.</summary>
public sealed class GoldWorldBankSnapshot
{
    private readonly byte[] _payload;
    private readonly int _consumed;
    private GoldWorldBankSnapshot(ushort opcode, byte[] payload, int consumed, GoldWorldBankSection[] sections, GoldWorldBankGlobals? globals, bool serverSnapshot)
    {
        Opcode = opcode;
        _payload = payload;
        _consumed = consumed;
        Sections = Array.AsReadOnly(sections);
        Globals = globals;
        IsServerSnapshot = serverSnapshot;
    }

    public ushort Opcode { get; }
    public bool IsServerSnapshot { get; }
    public int PayloadSize => _payload.Length;
    public IReadOnlyList<GoldWorldBankSection> Sections { get; }
    public GoldWorldBankGlobals? Globals { get; }
    public ReadOnlySpan<byte> RemainingData => _payload.AsSpan(_consumed);
    public uint GetHeaderWord(int index)
    {
        if ((uint)index >= GoldWorldBankLayout.HeaderWordCount) throw new ArgumentOutOfRangeException(nameof(index));
        return BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(index * sizeof(uint)));
    }
    public byte[] ToPayload() => _payload.ToArray();

    /// <summary>Parse the client-host upload form. Use FromServerPayload for a participant response.</summary>
    public static GoldWorldBankSnapshot FromPayload(ushort opcode, ReadOnlySpan<byte> payload) => Read(opcode, payload, false);
    public static GoldWorldBankSnapshot FromServerPayload(ushort opcode, ReadOnlySpan<byte> payload) => Read(opcode, payload, true);

    private static GoldWorldBankSnapshot Read(ushort opcode, ReadOnlySpan<byte> payload, bool serverSnapshot)
    {
        // Check supported layout before copying; no caller-owned storage escapes into the replica.
        GoldWorldBankLayout.GetSectionKind(opcode, 0);
        if (payload.Length < GoldWorldBankLayout.HeaderSize) throw new InvalidDataException("Gold world bank is missing its 40-byte header.");
        var bytes = payload.ToArray();
        var reader = new GoldWorldBankReader(bytes, GoldWorldBankLayout.HeaderSize);
        var globals = opcode == GoldWorldBankLayout.Bank1Opcode ? new GoldWorldBankGlobals(reader.ReadBlock(GoldWorldBankGlobals.Size)) : null;
        var sections = new GoldWorldBankSection[GoldWorldBankLayout.GetSectionCount(opcode)];
        for (var index = 0; index < sections.Length; index++)
        {
            var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint)));
            if (opcode == GoldWorldBankLayout.Bank4Opcode && index == GoldActualQuestsLayout.BankSection && count > GoldActualQuestsLayout.Size)
                throw new InvalidDataException("Gold actual-quest table exceeds 128 bytes.");
            var kind = GoldWorldBankLayout.GetSectionKind(opcode, index);
            var elementSize = serverSnapshot ? GoldWorldBankLayout.GetServerElementSize(opcode, index) : GoldWorldBankLayout.GetElementSize(opcode, index);
            var minimumRecordSize = GoldWorldBankRecordReader.GetMinimumSize(kind, elementSize);
            reader.RequireRecords(count, minimumRecordSize);
            var records = new GoldWorldBankRecord[(int)count];
            for (var record = 0; record < records.Length; record++)
            {
                records[record] = GoldWorldBankRecordReader.Read(kind, elementSize, ref reader);
            }
            sections[index] = new(index, kind, elementSize, records);
        }
        // Native consumers leave trailing bytes unread; do not invent an exact-length requirement.
        return new(opcode, bytes, reader.Cursor, sections, globals, serverSnapshot);
    }
}
