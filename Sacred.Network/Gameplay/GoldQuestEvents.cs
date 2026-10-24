using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public abstract record GoldQuestEvent(GoldMessage Message) : GoldScriptWorldEvent(Message);
public sealed record GoldQuestProgressEvent(GoldMessage Message, int HeaderIndex, IReadOnlyList<uint> Entries,
    uint DatabaseIndex) : GoldQuestEvent(Message);
public sealed record GoldQuestInsertEvent(GoldMessage Message, uint ObjectReference,
    GoldWorldBankRecord Record) : GoldQuestEvent(Message);
public sealed record GoldQuestUpdateEvent(GoldMessage Message, uint Index, GoldWorldBankRecord Record,
    ReadOnlyMemory<byte> HeaderEntries) : GoldQuestEvent(Message);
