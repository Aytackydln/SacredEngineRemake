using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public abstract record GoldCreatureCommandEvent(GoldMessage Message) : GoldGameEvent(Message);
public sealed record GoldCreatureDefinitionEvent(GoldMessage Message, uint UnreadWord, uint ContextReference, ReadOnlyMemory<byte> Definition)
    : GoldCreatureCommandEvent(Message);
public sealed record GoldCreatureValueCommandEvent(GoldMessage Message, uint ObjectReference, IReadOnlyList<uint> NativeArguments)
    : GoldCreatureCommandEvent(Message);
public sealed record GoldCreatureLinkEvent(GoldMessage Message, uint ParentReference, uint ChildReference, bool Option)
    : GoldCreatureCommandEvent(Message)
{
    public bool AddsLink => Message.Opcode == GoldCreatureCommandCodec.AddLinkOpcode;
}
public sealed record GoldPhysicalHitEvent(GoldMessage Message, uint ObjectReference, ReadOnlyMemory<byte> Data)
    : GoldCreatureCommandEvent(Message);
public sealed record GoldMountTransitionEvent(GoldMessage Message, uint ObjectReference, uint PartnerReference)
    : GoldCreatureCommandEvent(Message)
{
    public bool Mounts => Message.Opcode == GoldCreatureCommandCodec.MountOpcode;
    public uint NativeEventId => Mounts ? 0x209u : 0x20Au;
}
public sealed record GoldFxTargetEvent(GoldMessage Message, uint FxReference, uint TargetReference, GoldFxVectorBits Vector, uint Parameter)
    : GoldCreatureCommandEvent(Message);
public sealed record GoldLocalHitCheckEvent(GoldMessage Message, uint PlayerReference, ReadOnlyMemory<byte> Data)
    : GoldCreatureCommandEvent(Message);
public sealed record GoldDialogRegistrationEvent(GoldMessage Message, uint Resource, uint DialogIndex, uint QuestWord, string Name)
    : GoldCreatureCommandEvent(Message);

/// <summary>Verified fixed reads of Gold creature commands. Native process pointers in copied blocks remain opaque.</summary>
public static class GoldCreatureCommandCodec
{
    public const ushort DefinitionOpcode = 0x114;
    public const ushort ValueOpcode = 0x125;
    public const ushort RemoveLinkOpcode = 0x12E;
    public const ushort AddLinkOpcode = 0x12F;
    public const ushort PhysicalHitOpcode = 0x13C;
    public const ushort DismountOpcode = 0x15E;
    public const ushort MountOpcode = 0x160;
    public const ushort FxTargetOpcode = 0x177;
    public const ushort LocalHitCheckOpcode = 0x180;
    public const ushort DialogOpcode = 0x1CD;
    public const int DefinitionSize = 348;
    public const int PhysicalHitSize = 72;
    public const int LocalHitSize = 32;
    public static GoldCreatureCommandEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var size = message.Opcode switch
        {
            DefinitionOpcode => 8 + DefinitionSize, ValueOpcode => 28,
            RemoveLinkOpcode or MountOpcode or DismountOpcode => 8, AddLinkOpcode => 12,
            PhysicalHitOpcode => 4 + PhysicalHitSize, FxTargetOpcode => 24,
            LocalHitCheckOpcode => 4 + LocalHitSize, DialogOpcode => 13,
            _ => throw new InvalidDataException("Not a Gold creature command.")
        };
        if (message.Payload.Length < size) throw new InvalidDataException("Truncated Gold creature command.");
        var owned = message with { Payload = message.Payload.ToArray() };
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(offset));
        switch (message.Opcode)
        {
            case DefinitionOpcode: return new GoldCreatureDefinitionEvent(owned, Word(0), Word(4), owned.Payload.AsMemory(8, DefinitionSize));
            case ValueOpcode: return new GoldCreatureValueCommandEvent(owned, Word(0), Array.AsReadOnly(new[] { Word(4), Word(8), Word(12), Word(16), Word(20), 0u, Word(24), 0u }));
            case RemoveLinkOpcode or AddLinkOpcode: return new GoldCreatureLinkEvent(owned, Word(0), Word(4), message.Opcode == AddLinkOpcode && Word(8) != 0);
            case PhysicalHitOpcode: return new GoldPhysicalHitEvent(owned, Word(0), owned.Payload.AsMemory(4, PhysicalHitSize));
            case DismountOpcode or MountOpcode: return new GoldMountTransitionEvent(owned, Word(0), Word(4));
            case FxTargetOpcode: return new GoldFxTargetEvent(owned, Word(0), Word(4), new(Word(8), Word(12), Word(16)), Word(20));
            case LocalHitCheckOpcode: return new GoldLocalHitCheckEvent(owned, Word(0), owned.Payload.AsMemory(4, LocalHitSize));
            default:
                var name = owned.Payload.AsSpan(12); var end = name.IndexOf((byte)0);
                if (end < 0) throw new InvalidDataException("Unterminated Gold dialog registration name.");
                return new GoldDialogRegistrationEvent(owned, Word(0), Word(4), Word(8), GoldScriptNames.Read(name));
        }
    }
}
