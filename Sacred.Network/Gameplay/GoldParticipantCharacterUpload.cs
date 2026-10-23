using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Immutable, validated character-data sequence from complete runtime-layout streams.
/// Native 0x6058A0 uploads saved objects; 0x60BD03 then uploads hero and both inventories.
/// Upload/start controls and derived-value calculation are outside this bundle.</summary>
public sealed class GoldParticipantCharacterUpload
{
    private readonly GoldMessage[] _messages;
    /// <summary>Original full progression inputs omitted from the compact hero packet.</summary>
    public SacredSavedCreatureValues CharacterValues { get; }
    public IReadOnlyDictionary<uint, SacredSavedCreatureThinkState> CreatureThinking { get; private init; }
        = FrozenDictionary<uint, SacredSavedCreatureThinkState>.Empty;
    private GoldParticipantCharacterUpload(byte slot, int objects, GoldMessage[] messages, SacredSavedCreatureValues characterValues)
        => (PlayerSlot, ObjectCount, _messages, CharacterValues) = (slot, objects, messages, characterValues);
    public byte PlayerSlot { get; }
    public int ObjectCount { get; }
    public int MessageCount => _messages.Length;
    public long PayloadBytes => _messages.Sum(message => (long)message.Payload.Length);

    /// <summary>States follow nonempty saved-object slot order and must have participant identities applied.
    /// The byte schemas cannot establish whether price, stats or animation were normalized correctly.</summary>
    public static GoldParticipantCharacterUpload Create(GoldSavedCharacterReferencePlan plan,
        IReadOnlyList<byte[]> objectStates, ReadOnlySpan<byte> heroState,
        SacredSavedInventory inventory, SacredSavedInventory sharedInventory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(objectStates);
        if (objectStates.Count != plan.ObjectCount)
            throw new InvalidDataException("Participant upload must contain every planned saved object exactly once.");
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(sharedInventory);
        var owned = plan.References.Values.ToHashSet();
        var messages = new List<GoldMessage>(checked(plan.ObjectCount + 3));
        var index = 0;
        foreach (var record in plan.Source.Objects.Records)
        {
            if (record.IsEmpty) continue;
            var payload = objectStates[index++];
            ArgumentNullException.ThrowIfNull(payload);
            RequireSize(payload, 8);
            ValidateIdentity(payload, plan.References[record.BaseState.ObjectReference], record.BaseState.Resource, plan.PlayerSlot);
            if (record.Item is { } savedItem)
            {
                var item = GoldObjectStateReader.ReadItem(payload) ?? throw new InvalidDataException("Saved item requires a complete Gold item stream.");
                if (!item.RemainingData.IsEmpty || item.HasWeaponExtension != savedItem.HasWeaponExtension || item.Count != savedItem.Count)
                    throw new InvalidDataException("Participant item stream has an unmatched subtype, count or suffix.");
            }
            else if (record.Creature is not null) ValidateCreature(payload, 0x16E, owned);
            else throw new InvalidDataException("Unsupported participant object body.");
            messages.Add(GoldObjectStateCodec.Create(0x16E, plan.PlayerSlot, payload));
        }
        RequireSize(heroState, 24);
        ValidateIdentity(heroState, plan.PlayerSlot, plan.Source.Hero.Creature.BaseState.Resource, plan.PlayerSlot);
        ValidateCreature(heroState, 0x169, owned);
        messages.Add(GoldObjectStateCodec.Create(0x169, plan.PlayerSlot, heroState));
        ValidateInventory(inventory, plan.PlayerSlot, owned);
        ValidateInventory(sharedInventory, 17, owned);
        messages.Add(GoldInventoryCodec.Create(plan.PlayerSlot, plan.PlayerSlot, inventory));
        messages.Add(GoldInventoryCodec.Create(plan.PlayerSlot, 17, sharedInventory));
        foreach (var message in messages) RequireSize(message.Payload, 0);
        var thinking = new Dictionary<uint, SacredSavedCreatureThinkState> { [plan.PlayerSlot] = plan.Source.Hero.Creature.Thinking };
        foreach (var record in plan.Source.Objects.Records)
            if (record.Creature is { } creature) thinking[plan.References[record.BaseState.ObjectReference]] = creature.Thinking;
        return new(plan.PlayerSlot, plan.ObjectCount, messages.ToArray(), plan.Source.Hero.Creature.Values)
        { CreatureThinking = new ReadOnlyDictionary<uint, SacredSavedCreatureThinkState>(thinking) };
    }

    /// <summary>Return isolated message payloads. Transport assigns wire sequences when sending.</summary>
    public IReadOnlyList<GoldMessage> CreateMessages() => Array.AsReadOnly(_messages.Select(message =>
        message with { Payload = message.Payload.ToArray() }).ToArray());

    private static void ValidateIdentity(ReadOnlySpan<byte> payload, uint reference, uint resource, byte slot)
    {
        var state = GoldObjectBaseState.FromPayload(payload);
        if (state.ObjectReference != reference || state.ObjectType != resource || state.Owner != slot)
            throw new InvalidDataException("Participant object identity/resource/owner differs from its reference plan.");
    }

    private static void ValidateCreature(ReadOnlySpan<byte> payload, ushort opcode, HashSet<uint> owned)
    {
        var creature = GoldObjectStateReader.ReadCreature(payload) ?? throw new InvalidDataException("Participant creature requires a complete Gold creature stream.");
        var tail = creature.ReadTail(opcode) ?? throw new InvalidDataException("Participant creature is missing values and trailer.");
        if (!tail.RemainingData.IsEmpty) throw new InvalidDataException("Participant creature has unparsed trailing bytes.");
        foreach (var reference in creature.EquipmentReferences)
            if (reference != 0 && !owned.Contains(reference)) throw new InvalidDataException("Participant creature contains a foreign equipment reference.");
        if (tail.MountPartnerReference != 0 && !owned.Contains(tail.MountPartnerReference))
            throw new InvalidDataException("Participant creature contains a foreign mount partner reference.");
    }

    private static void RequireSize(ReadOnlySpan<byte> payload, int prefix)
    {
        if (payload.Length > GoldMessageCodec.MaximumMessageBytes - GoldMessageCodec.HeaderBytes - prefix)
            throw new InvalidDataException("Participant object/inventory exceeds the Gold message size limit.");
    }

    private static void ValidateInventory(SacredSavedInventory inventory, ushort id, HashSet<uint> owned)
    {
        if (inventory.InventoryId != id || !inventory.RemainingData.IsEmpty)
            throw new InvalidDataException("Participant inventory identity or extent differs from its target.");
        for (var index = 0; index < SacredSavedInventoryLayout.CellCount; index++)
            RequireOwned(inventory.GetCell(index).ObjectReference, owned);
        for (var index = 0; index < SacredSavedInventoryLayout.ItemBarCount; index++)
            RequireOwned(BinaryPrimitives.ReadUInt32LittleEndian(inventory.GetItemBarRecord(index)), owned);
    }

    private static void RequireOwned(uint reference, HashSet<uint> owned)
    {
        if (reference != 0 && !owned.Contains(reference))
            throw new InvalidDataException("Participant inventory contains a foreign object reference.");
    }
}
