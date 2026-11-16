using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Admission;

internal static class AdmissionControllerSetupChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-admission-controller-setup.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("constructors").EnumerateArray())
        {
            var snapshot = WorldAdmissionControllerSetup.Construct(row.GetProperty("local_reference").GetUInt32());
            Compare(snapshot, row.GetProperty("fields"));
            Check(snapshot.MultiplayerByte is null, "construction does not establish the separate multiplayer global");
            Check(row.GetProperty("fields").GetProperty("multiplayer").GetByte() == row.GetProperty("multiplayer_before").GetByte(),
                "original constructor preserves the independently supplied multiplayer global");
        }
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var fill = row.GetProperty("fill").GetByte(); var tail = row.GetProperty("tail").GetByte();
            var previous = new WorldAdmissionControllerSnapshot(new(fill, fill, uint.MaxValue, uint.MaxValue, uint.MaxValue),
                GoldPlayerRoster.FromPayload(Enumerable.Repeat(fill, GoldPlayerRosterLayout.Size).ToArray()),
                new(fill, fill, fill, fill), new(tail, tail, tail, tail, tail, tail), null);
            var snapshot = WorldAdmissionControllerSetup.Initialize(previous, row.GetProperty("mode").GetByte(),
                row.GetProperty("result").GetByte(), row.GetProperty("local_reference").GetUInt32());
            Compare(snapshot, row.GetProperty("fields"));
            Check(snapshot.MultiplayerByte == row.GetProperty("fields").GetProperty("multiplayer").GetByte() &&
                snapshot.Gates.ActiveByte0 == row.GetProperty("returned").GetByte(), "native connection result copied to activity/global/return");
            var inputs = snapshot.CreateInputs(false, true, new(0, 0, 0, 0, 0, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty));
            Check(inputs.ControllerFlag381B == (tail != 0) && !inputs.ControllerFlag9 && !inputs.ControllerFlag28 &&
                inputs.Multiplayer == (snapshot.MultiplayerByte != 0), "raw initialized/preserved gates projected without wire-role inference");
        }
        return checks;

        void Compare(WorldAdmissionControllerSnapshot snapshot, JsonElement expected)
        {
            var header = Convert.FromHexString(expected.GetProperty("header").GetString()!);
            var tail = Convert.FromHexString(expected.GetProperty("tail").GetString()!);
            Check(snapshot.State.PendingByte3 == header[3] && snapshot.State.HostByte1 == header[1] &&
                snapshot.State.LocalReference == Word(header, 0x14) && snapshot.State.HostToken == Word(header, 0x24) &&
                snapshot.State.ConnectionLocalReference == expected.GetProperty("connection_local").GetUInt32(), "native identity/header projection");
            Check(snapshot.Gates.ActiveByte0 == header[0] && snapshot.Gates.Flag2 == header[2] &&
                snapshot.Gates.Flag9 == header[9] && snapshot.Gates.Flag28 == header[0x28], "native activity/admission gates");
            var service = snapshot.ServiceFlags;
            Check(new[] { service.Byte3818, service.Byte3819, service.Byte381A, service.Byte381B, service.Byte381C, service.Byte381D }.SequenceEqual(tail),
                "complete original tail write/preservation profile");
            Check(Convert.ToHexString(SHA256.HashData(snapshot.Roster.ToPayload())).Equals(expected.GetProperty("roster_sha256").GetString(), StringComparison.OrdinalIgnoreCase),
                "complete native roster clear, independent of dirty prior records");
        }
        void Check(bool pass, string why)
        { if (!pass) throw new Exception("Controller setup: " + why); checks++; }
    }
    private static uint Word(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
}
