using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Network.Admission;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold dispatch only. The Demo NW enum and lobby IDs belong to other protocols.</summary>
public static class GoldGameEventDecoder
{
    public static GoldGameEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payload = message.Payload.AsSpan();
        switch (message.Opcode)
        {
            case GoldSpecialMoveLayout.Opcode:
                return GoldSpecialMoveCodec.Decode(message);
            case GoldObjectFxSignalCodec.RemoveOpcode:
            case GoldObjectFxSignalCodec.UsedOpcode:
                return GoldObjectFxSignalCodec.Decode(message);
            case GoldCombatLayout.HealthOpcode:
            case GoldCombatLayout.DamageOpcode:
            case GoldCombatLayout.BowOpcode:
            case GoldCombatLayout.BattleOpcode:
                return GoldCombatCodec.Decode(message);
            case GoldObjectFlightStopCodec.Opcode:
                return GoldObjectFlightStopCodec.Decode(message);
            case GoldItemPickupCodec.RequestOpcode:
            case GoldItemPickupCodec.TakenOpcode:
                return GoldItemPickupCodec.Decode(message);
            case GoldObjectFlightLayout.DropOpcode:
            case GoldObjectFlightLayout.ForcedOpcode:
                return GoldObjectFlightCodec.Decode(message);
            case GoldContainerActivationCodec.Opcode:
                return GoldContainerActivationCodec.Decode(message);
            case GoldNpcDialogActionLayout.BeginOpcode:
            case GoldNpcDialogActionLayout.EndOpcode:
                return GoldNpcDialogActionCodec.Decode(message);
            case GoldNpcIdleAnimationLayout.Opcode:
                return GoldNpcIdleAnimationCodec.Decode(message);
            case GoldScriptDialogReplica.UpdateOpcode:
                return GoldScriptDialogReplica.Decode(message);
            case GoldWorldTriggerCodec.CreateOpcode:
            case GoldWorldTriggerCodec.ClearOpcode:
            case GoldWorldTriggerCodec.SetOpcode:
                return GoldWorldTriggerCodec.Decode(message);
            case GoldScriptResourceLayout.RemoveNameOpcode:
            case GoldScriptResourceLayout.RegisterNameOpcode:
                return GoldScriptResourceCodec.Decode(message);
            case GoldWorldEnvironmentLayout.Opcode:
                RequireExact(payload, message.Sender == 0 ? GoldWorldEnvironmentLayout.Size : 0, message.Opcode);
                return new GoldWorldEnvironmentEvent(message with { Payload = payload.ToArray() });
            case GoldObjectLifecycleLayout.RemoveOpcode:
            case GoldObjectLifecycleLayout.ImmediateRemoveOpcode:
            case GoldObjectLifecycleLayout.OwnerOpcode:
                return GoldObjectLifecycleCodec.Decode(message);
            case GoldWorldRegionFlagsCodec.SetOpcode:
            case GoldWorldRegionFlagsCodec.ClearOpcode:
                return GoldWorldRegionFlagsCodec.Decode(message);
            case GoldScriptVariableLayout.AppendOpcode:
            case GoldScriptVariableLayout.UpdateOpcode:
                return GoldScriptVariableCodec.Decode(message);
            case GoldWorldBankLayout.Bank1Opcode:
            case GoldWorldBankLayout.Bank2Opcode:
            case GoldWorldBankLayout.Bank3Opcode:
            case GoldWorldBankLayout.Bank4Opcode:
                return GoldWorldBankCodec.Decode(message);
            case GoldEquipmentRemovalLayout.Opcode:
                return GoldEquipmentRemovalCodec.Decode(message);
            case GoldEquipmentAssignmentLayout.Opcode:
                return GoldEquipmentAssignmentCodec.Decode(message);
            case GoldCreatureMountLayout.Opcode:
                return GoldCreatureMountCodec.Decode(message);
            case GoldMovementTriggerDeletionLayout.Opcode:
                return GoldMovementTriggerDeletionCodec.Decode(message);
            case GoldPlayerStatisticsLayout.Opcode:
                return GoldPlayerStatisticsCodec.Decode(message);
            case GoldObjectTriggerLayout.Opcode:
                return GoldObjectTriggerCodec.Decode(message);
            case GoldDeathScriptLayout.Opcode:
                return GoldDeathScriptCodec.Decode(message);
            case GoldCreatureUpdateLayout.StateOpcode:
            case GoldCreatureUpdateLayout.ValuesOpcode:
                return GoldCreatureUpdateCodec.Decode(message);
            case GoldObjectPositionCodec.Opcode:
                return GoldObjectPositionCodec.Decode(message);
            case GoldWorldEntryCodec.ParticipantUploadOpcode:
            case GoldWorldEntryCodec.HostUploadOpcode:
                RequireExact(payload, 0, message.Opcode);
                return new GoldWorldUploadEvent(message, message.Opcode == GoldWorldEntryCodec.HostUploadOpcode);
            case GoldWorldEntryCodec.ParticipantStartOpcode:
                RequireExact(payload, 0, message.Opcode);
                return message.Sender == 0 ? new GoldParticipantStartApprovedEvent(message) : new GoldParticipantStartRequestEvent(message);
            case GoldWorldEntryCodec.HostStartOpcode:
                if (message.Sender == 0)
                {
                    RequireExact(payload, 0, message.Opcode);
                    return new GoldHostWorldAvailableEvent(message);
                }
                RequireExact(payload, 4, message.Opcode);
                return new GoldHostStartRequestEvent(message, Word(payload, 0));
            case GoldWorldEntryCodec.PeerReadyOpcode:
                RequireExact(payload, 0, message.Opcode);
                return new GoldPeerReadyEvent(message);
            case GoldSectorEntryLayout.Opcode:
                return message.Sender == 0 ? new GoldSectorEntryEvent(message, GoldSectorEntry.FromPayload(payload)) :
                    new GoldSectorInterestEvent(message, GoldSectorInterestRequest.FromPayload(payload));
            case GoldInventoryCodec.Opcode:
                Require(payload, 4 + SacredSavedInventoryLayout.MinimumSize, message.Opcode);
                return new GoldInventoryEvent(message, Word(payload, 0), SacredSavedInventory.FromPayload(payload[4..]));
            case GoldFirstContactLayout.Opcode:
                return new GoldFirstContactEvent(message, GoldFirstContact.FromPayload(payload));
            case GoldPlayerDataLayout.Opcode:
                if (payload.Length == GoldPlayerRosterLayout.Size)
                    return new GoldPlayerRosterEvent(message, GoldPlayerRoster.FromPayload(payload));
                return new GoldPlayerDataEvent(message, GoldPlayerData.FromPayload(payload));
            case 0x11:
                RequireExact(payload, 6, message.Opcode);
                if (payload[1] is < 1 or > 16) throw new InvalidDataException("Invalid Sacred player slot.");
                return new GoldAdmissionEvent(message, payload[0], payload[1], Word(payload, 2));
            case 0x3F:
                RequireExact(payload, 8, message.Opcode);
                return new GoldRejectionEvent(message, Word(payload, 0), Word(payload, 4));
            case 0x13:
            case 0x14:
                RequireExact(payload, 0, message.Opcode);
                return new GoldPasswordResultEvent(message, message.Opcode == 0x14);
            case GoldTimeProbeCodec.RequestOpcode:
                RequireExact(payload, GoldTimeProbeCodec.RequestBytes, message.Opcode);
                return new GoldTimeProbeEvent(message, Word(payload, 0), Word(payload, 4), Word(payload, 8), Word(payload, 12));
            case GoldReferenceCodec.BatchOpcode:
                if (message.Sender == 0)
                    return new GoldReferenceBatchEvent(message, GoldReferenceBatch.FromPayload(payload));
                RequireExact(payload, 4, message.Opcode);
                var count = Word(payload, 0);
                if (message.Sender > 16 || count is 0 or >= GoldReferenceCodec.ReferenceLimit)
                    throw new InvalidDataException("Invalid Gold object-reference request.");
                return new GoldReferenceRequestEvent(message, count);
            case GoldReferenceCodec.RangeOpcode:
                RequireExact(payload, 8, message.Opcode);
                if (message.Sender != 0)
                {
                    if (message.Sender > 16) throw new InvalidDataException("Invalid Gold trigger-reference request sender.");
                    return new GoldTriggerRangeRequestEvent(message, Word(payload, 0), Word(payload, 4));
                }
                return new GoldReferenceRangeEvent(message, Word(payload, 0), Word(payload, 4));
            // GameServer 0x4962A0 (start/update), 0x496AB0 (end).
            case 0x161:
            case 0x163:
                Require(payload, 24, message.Opcode);
                if (payload.Length > GoldMovementLayout.PrefixSize)
                    _ = GoldMovementBody.FromPayload(message.Opcode, payload[GoldMovementLayout.PrefixSize..]);
                return new GoldMovementEvent(message, Word(payload, 0), Word(payload, 4), Word(payload, 8),
                    Word(payload, 12), Word(payload, 16), Word(payload, 20), payload[24..].ToArray());
            case 0x162:
                Require(payload, 24, message.Opcode);
                return new GoldMovementEndEvent(message, Word(payload, 0), Word(payload, 4), Word(payload, 8),
                    Word(payload, 12), Word(payload, 16), Word(payload, 20), payload[24..].ToArray());
            case 0x164:
                Require(payload, 4, message.Opcode);
                return new GoldIdleEvent(message, Word(payload, 0), payload[4..].ToArray());
            case GoldDoorLayout.CloseOpcode:
            case GoldDoorLayout.OpenOpcode:
                return GoldDoorCodec.Decode(message);
            case 0x169:
            case 0x16B:
            case 0x16F:
                RequireObjectState(payload, 24, message.Opcode);
                return new GoldObjectEvent(message, Word(payload, 0), Word(payload, 4),
                    [Word(payload, 8), Word(payload, 12), Word(payload, 16), Word(payload, 20)], payload[24..].ToArray());
            case 0x16C:
            case 0x16E:
                RequireObjectState(payload, 8, message.Opcode);
                return new GoldObjectEvent(message, Word(payload, 0), Word(payload, 4), [], payload[8..].ToArray());
            default:
                return new GoldUnknownEvent(message);
        }
    }

    private static uint Word(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static void RequireObjectState(ReadOnlySpan<byte> payload, int prefixSize, ushort opcode)
    {
        Require(payload, prefixSize + GoldObjectStateLayout.Size, opcode);
        GoldObjectStateReader.Validate(payload[prefixSize..], opcode);
    }
    private static void RequireExact(ReadOnlySpan<byte> bytes, int size, ushort opcode)
    {
        if (bytes.Length != size) throw new InvalidDataException($"Gold opcode 0x{opcode:X4} needs exactly {size} payload bytes.");
    }
    private static void Require(ReadOnlySpan<byte> bytes, int minimum, ushort opcode)
    {
        if (bytes.Length < minimum) throw new InvalidDataException($"Gold opcode 0x{opcode:X4} needs at least {minimum} payload bytes.");
    }
}
