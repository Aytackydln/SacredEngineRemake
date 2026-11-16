using Sacred.Network.Admission;

namespace Sacred.GameplayHost.Networking;

/// <summary>Capture established current storage, without invoking construction
/// or inferring multiplayer from mode/host bits. Accessors are pure and caller
/// owned. A missing connection cannot fabricate a connection-local reference.</summary>
public static class WorldAdmissionControllerCapture
{
    public static WorldAdmissionControllerSnapshot Capture(WorldControlMemory controller,
        Func<WorldControlMemory?> readConnection, Func<byte?> readMultiplayer)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(readConnection);
        ArgumentNullException.ThrowIfNull(readMultiplayer);
        var connection = readConnection() ?? throw new NotSupportedException("Controller capture requires current connection storage.");
        var roster = new byte[GoldPlayerRosterLayout.Size];
        for (var index = 0; index < roster.Length; index++) roster[index] = controller.ReadByte(0x54 + (uint)index);
        return new(new(controller.ReadByte(3), controller.ReadByte(1), controller.ReadWord(0x14),
                controller.ReadWord(0x24), connection.ReadWord(0x48)), GoldPlayerRoster.FromPayload(roster),
            new(controller.ReadByte(0), controller.ReadByte(2), controller.ReadByte(9), controller.ReadByte(0x28)),
            new(controller.ReadByte(0x3818), controller.ReadByte(0x3819), controller.ReadByte(0x381a),
                controller.ReadByte(0x381b), controller.ReadByte(0x381c), controller.ReadByte(0x381d)), readMultiplayer());
    }
}
