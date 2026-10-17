using Sacred.Core.World;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Prepared first-host wire state. Validates mandatory banks, character data and known
/// object links before transmission. This cannot establish authored execution or value correctness;
/// GameplayHost must finish those before supplying the sequence. Reservation negotiation is separate.</summary>
public sealed class GoldHostWorldUpload
{
    private readonly GoldMessage[] _messages;
    private GoldHostWorldUpload(byte slot, uint sectorTableSize, GoldMessage[] messages, int objects)
        => (PlayerSlot, SectorTableSize, _messages, ObjectCount) = (slot, sectorTableSize, messages, objects);
    public byte PlayerSlot { get; }
    public uint SectorTableSize { get; }
    public int ObjectCount { get; }
    public int MessageCount => _messages.Length;
    public long PayloadBytes => _messages.Sum(message => (long)message.Payload.Length);

    /// <summary>Retain executed world-effect order around the contiguous native bank order
    /// 1B2, 1B4, 1B3, 1B1, including sector objects/callbacks after banks. Controls are added by the session.
    /// allocatedReferences must come from the shared hero/world reservation allocator.</summary>
    public static GoldHostWorldUpload Create(GoldParticipantCharacterUpload character,
        IReadOnlyList<GoldMessage> worldBeforeBanks, IReadOnlyList<GoldWorldBankSnapshot> banks,
        IReadOnlyList<GoldMessage> worldAfterBanks, SacredSectorIndex sectors, IReadOnlyCollection<uint> allocatedReferences)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(worldBeforeBanks);
        ArgumentNullException.ThrowIfNull(banks);
        ArgumentNullException.ThrowIfNull(worldAfterBanks);
        ArgumentNullException.ThrowIfNull(sectors);
        ArgumentNullException.ThrowIfNull(allocatedReferences);
        ushort[] order = [GoldWorldBankLayout.Bank2Opcode, GoldWorldBankLayout.Bank4Opcode,
            GoldWorldBankLayout.Bank3Opcode, GoldWorldBankLayout.Bank1Opcode];
        if (!banks.Select(bank => bank.Opcode).SequenceEqual(order))
            throw new InvalidDataException("Host upload requires all four banks exactly once in native order.");
        if (banks.Any(bank => !bank.RemainingData.IsEmpty))
            throw new InvalidDataException("Generated host banks cannot contain unresolved trailing bytes.");
        if (worldBeforeBanks.Concat(worldAfterBanks).Any(message => order.Contains(message.Opcode)))
            throw new InvalidDataException("World effects cannot contain additional bank messages.");
        var slot = character.PlayerSlot;
        var messages = character.CreateMessages().Concat(worldBeforeBanks)
            .Concat(banks.Select(bank => GoldWorldBankCodec.Create(slot, slot, bank)))
            .Concat(worldAfterBanks).Select(message => Copy(message, slot)).ToArray();
        var graph = GoldHostUploadReferenceGraph.Validate(slot, messages, allocatedReferences);
        var interests = worldBeforeBanks.Where(message => message.Opcode == GoldSectorInterestLayout.Opcode).ToArray();
        if (interests.Length != 1 || worldBeforeBanks.Count == 0 || worldBeforeBanks[^1].Opcode != GoldSectorInterestLayout.Opcode)
            throw new InvalidDataException("The prepared first-host prefix must end with its initial sector interest request.");
        if (worldAfterBanks.Any(message => message.Opcode == GoldSectorInterestLayout.Opcode))
            throw new InvalidDataException("Initial host sector interest precedes world banks.");
        var interest = GoldSectorInterestRequest.FromPayload(interests[0].Payload);
        if (interest.CurrentSector != graph.HeroSector)
            throw new InvalidDataException("Initial host sector interest does not match its hero.");
        return new(slot, sectors.RuntimeTableSize, messages, graph.ObjectCount);
    }

    public IReadOnlyList<GoldMessage> CreateMessages() => Array.AsReadOnly(_messages.Select(message =>
        message with { Payload = message.Payload.ToArray() }).ToArray());

    private static GoldMessage Copy(GoldMessage message, byte slot)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(message.Payload);
        if (message.Sender != slot || message.Origin != slot)
            throw new InvalidDataException("Host data must use the admitted host's sender and origin.");
        if (message.Payload.Length > GoldMessageCodec.MaximumMessageBytes - GoldMessageCodec.HeaderBytes)
            throw new InvalidDataException("Host upload message exceeds the Gold size limit.");
        return message with { Sequence = 0, Payload = message.Payload.ToArray() };
    }
}
