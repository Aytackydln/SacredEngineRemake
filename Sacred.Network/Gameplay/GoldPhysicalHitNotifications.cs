using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

public sealed record GoldHitNotification(uint NativeEventId, GoldReplicatedObject Object, IReadOnlyList<uint> Arguments);

internal static class GoldPhysicalHitNotifications
{
    public static IReadOnlyList<GoldHitNotification> Create(GoldPhysicalHitEvent entry, GoldReplicatedObject subject,
        IReadOnlyDictionary<uint, GoldReplicatedObject> objects)
    {
        uint Word(int index) => BinaryPrimitives.ReadUInt32LittleEndian(entry.Data.Span[(index * 4)..]);
        var first = Enumerable.Range(0, 13).Select(Word).Append(Word(16)).ToArray();
        var requests = new List<GoldHitNotification> { new(0x44, subject, Array.AsReadOnly(first)) };
        if (Word(13) != 0 && objects.TryGetValue(Word(16), out var other) && other.Creature is not null &&
            (other.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0)
        {
            var second = new uint[13];
            second[0] = Word(13); second[1] = Word(14); second[2] = Word(15);
            second[3] = Word(17) & 0xffff; second[4] = Word(17) >> 16;
            requests.Add(new(0x20d, other, Array.AsReadOnly(second)));
        }
        return requests.AsReadOnly();
    }
}
