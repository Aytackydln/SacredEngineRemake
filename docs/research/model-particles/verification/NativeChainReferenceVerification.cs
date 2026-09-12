using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Sacred.Granny.Meshes;
using Sacred.Inventory.Effects;
using Sacred.Particles;

internal static class NativeChainReferenceVerification
{
    public static void Run(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var type = typeof(EquipmentEffectScene).Assembly.GetType("Sacred.Inventory.Effects.NativeModelEffectSimulation")!;
        var update = type.GetMethod("Update")!;
        var advance = type.GetMethod("UpdateChain", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var positions = type.GetField("_positions", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var velocities = type.GetField("_velocities", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var checks = 0;
        var maximumError = 0f;
        foreach (var test in document.RootElement.EnumerateArray())
        {
            if (test.GetProperty("build").GetString() != "gold") continue;
            var kind = Enum.Parse<SacredModelEffectKind>(test.GetProperty("kind").GetString()!);
            var definition = SacredModelEffectCatalogue.Definitions.Single(d => d.Kind == kind);
            var surfaces = Enumerable.Range(0, 50).Select(i => new EquipmentEffectSurface(i * 6, 6,
                definition.TextureName, Vector4.One, ParticleTextureMode.NativeModelColored, 0)).ToArray();
            var simulation = Activator.CreateInstance(type,
                [definition, Vector3.Zero, Vector3.Zero, null, Vector3.UnitX, 0, surfaces])!;
            var mesh = new Mesh(new VertexPositionNormalTexture[200], Array.Empty<ushort>());
            update.Invoke(simulation, [mesh, null, 0f]);
            foreach (var step in test.GetProperty("steps").EnumerateArray())
            {
                var emitter = Vector(step.GetProperty("emitter"));
                advance.Invoke(simulation, [emitter, Vector3.UnitX, step.GetProperty("dt").GetSingle()]);
                var p = (Vector3[])positions.GetValue(simulation)!;
                var v = (Vector3[])velocities.GetValue(simulation)!;
                var i = 0;
                foreach (var point in step.GetProperty("points").EnumerateArray())
                {
                    var error = Math.Max(Vector3.Distance(p[i], Vector(point)), Vector3.Distance(v[i], Vector(point, 3)));
                    maximumError = Math.Max(maximumError, error);
                    if (error > .003f) throw new InvalidOperationException($"Native {kind} point {i} error {error}");
                    i++;
                    checks++;
                }
            }
        }
        if (checks != 600) throw new InvalidOperationException("Incomplete native chain fixtures");
        Console.WriteLine($"PASS {checks} native chain position/impulse comparisons; maximum error {maximumError:G4}");
    }

    private static Vector3 Vector(JsonElement array, int offset = 0) =>
        new(array[offset].GetSingle(), array[offset + 1].GetSingle(), array[offset + 2].GetSingle());
}
