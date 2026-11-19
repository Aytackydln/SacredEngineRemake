using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using Sacred.GameplayHost.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class HostHeroPhysicalImportChecks
{
    public static int Run(string root)
    {
        using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-host-hero-physical-import.json")));
        var data = proof.RootElement;
        var initial = Convert.FromHexString(data.GetProperty("initial_actor_hex").GetString()!);
        var projection = new SacredObjectProjection(Matrix(data.GetProperty("view")), Matrix(data.GetProperty("projection")));
        var rootValues = data.GetProperty("root").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        var rootPosition = new Vector3(rootValues[0], rootValues[1], rootValues[2]);
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            uint InitialWord(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(initial.AsSpan(offset));
            var actor = new WorldCreatureInitialActor(new(InitialWord(12), InitialWord(16), InitialWord(20), 0, 0, 0, 0, 0, 0, initial[56], initial[57], InitialWord(60)),
                new(initial.AsSpan(0x3b0, 238).ToArray(), initial.AsSpan(0x4d0, 24).ToArray(), 6, 0), 1, 0, 0, 0, 0, 0, []);
            var activation = new WorldObjectActivationQueue();
            activation.ActivateInitial(actor.Node, 0);
            var source = Convert.FromHexString(row.GetProperty("source_hex").GetString()!);
            var native = Convert.FromHexString(row.GetProperty("actor_hex").GetString()!);
            var active = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(55)) != 0;
            var restored = WorldCreatureSavedPhysicalImport.Restore(actor, source, active ? rootPosition : null, 1,
                row.GetProperty("global_speed").GetSingle(), projection);
            var physical = restored.RestoredPhysical!;
            uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(offset));
            short Short(int offset) => BinaryPrimitives.ReadInt16LittleEndian(native.AsSpan(offset));
            Vector3 Vector(int offset) => new(BitConverter.UInt32BitsToSingle(Word(offset)), BitConverter.UInt32BitsToSingle(Word(offset + 4)), BitConverter.UInt32BitsToSingle(Word(offset + 8)));
            Check(restored.Base.ObjectReference == Word(12) && restored.Base.ObjectType == Word(16) && restored.Base.Flags == Word(20), "native base identity and flags");
            Check(restored.Base.Sector == unchecked((ushort)Short(24)) && restored.Base.X == unchecked((int)Word(28)) && restored.Base.Y == unchecked((int)Word(32)) && restored.Base.Surface == native[36], "saved pose retained before entry placement");
            Check(restored.Base.Word28 == Word(40) && restored.Base.Word34 == Word(52) && restored.Base.Byte38 == native[56] && restored.Base.Owner == native[57] && restored.Base.Word3C == Word(60), "base words");
            Check(restored.NextOnTile == Word(48) && restored.StaticSurfaceReference == Word(44), "world links");
            Check(physical.Animation == Word(0x64) && physical.WordF0 == Word(0xf0) && physical.Word60 == Word(0x60) && physical.Word68 == Word(0x68) && physical.Word6C == Word(0x6c) && physical.PaletteMask == Word(0x5c), "physical words");
            Equal(restored.Direction, Vector(0x70), "direction");
            Equal(physical.Velocity, Vector(0xb4), "velocity");
            Equal(physical.AngularVelocity, Vector(0xc0), "angular velocity");
            Equal(physical.ExactPosition, Vector(0xcc), "exact position");
            Equal(physical.BoundsMinimum, Vector(0xd8), "minimum bounds");
            Equal(physical.BoundsMaximum, Vector(0xe4), "maximum bounds");
            Equal(physical.BonePosition, Vector(0x98), "bone position; final 36 file bytes discarded");
            Check(physical.QuestNumber == Short(0x94) && physical.QuestEffect == Short(0x96) && physical.Flags == Word(0xb0), "physical flags and quest words");
            Check(physical.Rotation == Vector(0x7c) && physical.RootOffset == Vector(0x88), "activation rotation and original root");
            Check(ReferenceEquals(actor.Node, restored.Node) && ReferenceEquals(actor.Component, restored.Component) && activation.Records.Count == 1, "same factory node/constructor component; no extra activation or normalization");
            if (active)
            {
                try
                {
                    _ = WorldCreatureSavedPhysicalImport.Restore(actor, source, null, 1, 1, projection);
                    throw new Exception("Missing model backend accepted.");
                }
                catch (InvalidOperationException) { Check(actor.RestoredPhysical is null, "missing backend leaves original actor unchanged"); }
                var parameters = row.GetProperty("parameters").EnumerateArray().ToArray();
                Check(physical.LoopCount == parameters[^2][1].GetSingle() && physical.PlaybackSpeed == parameters[^1][1].GetDouble(), "new animation loop and PC24 speed");
                var nativeBasis = Convert.FromHexString(row.GetProperty("orientations")[0].GetString()!);
                var matrix = physical.ModelOrientation!.Value;
                var actual = new[] { matrix.M11, matrix.M12, matrix.M13, matrix.M21, matrix.M22, matrix.M23, matrix.M31, matrix.M32, matrix.M33 };
                for (var index = 0; index < actual.Length; index++)
                    Check(MathF.Abs(actual[index] - BinaryPrimitives.ReadSingleLittleEndian(nativeBasis.AsSpan(index * 4))) < .000002f, "original model orientation basis");
            }
            else Check(physical.ModelOrientation is null && row.GetProperty("requests").GetArrayLength() == 0, "zero selection skips model creation");
        }
        return checks;
        void Check(bool pass, string reason) { if (!pass) throw new InvalidDataException("HostHeroPhysicalImport: " + reason); checks++; }
        void Equal(GoldPhysicalVector actual, Vector3 expected, string reason) => Check(new Vector3(actual.X, actual.Y, actual.Z) == expected, reason);
    }

    private static Matrix4x4 Matrix(JsonElement source)
    {
        var f = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        return new(f[0],f[1],f[2],f[3], f[4],f[5],f[6],f[7], f[8],f[9],f[10],f[11], f[12],f[13],f[14],f[15]);
    }
}
