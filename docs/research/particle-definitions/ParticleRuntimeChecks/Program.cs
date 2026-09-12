using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Particles;
using Sacred.Particles;
using Sacred.World.Geometry;
using Sacred.World.Particles;

using var reference = JsonDocument.Parse(File.ReadAllText(args[0]));
var initial = reference.RootElement.GetProperty("initial").EnumerateArray().Select(x => x.GetSingle()).ToArray();
var rates = reference.RootElement.GetProperty("motion").EnumerateArray().Select(x => x.GetSingle()).ToArray();
var emission = new byte[100];
void Write(int offset, float value) => BitConverter.TryWriteBytes(emission.AsSpan(offset), value);
Write(0, initial[9]); Write(8, initial[10]); Write(0x10, initial[12]); Write(0x18, initial[14]);
for (var i = 0; i < 3; i++) { Write(0x20 + i * 4, initial[6 + i]); Write(0x38 + i * 4, initial[i]); }
Write(0x58, 0.125f);
var set = new SacredParticleParameterSet(0, MemoryMarshal.Read<SacredParticleEmissionLayout>(emission),
    MemoryMarshal.Read<SacredParticleMotionLayout>(MemoryMarshal.AsBytes(rates.AsSpan())))
{
    Colors = Enumerable.Range(0, 256).Select(i => 0xFF000000u | (uint)i).ToArray()
};
var definition = new SacredParticleDefinition(0, "test", 0, 0, "test", 0,
    SacredParticleDefinitionStatus.Decoded, null, [], new("test", 0xC, 0), [set]) { UsesWind = true };
var creation = new SacredScriptCreateObject(null, 0, new(0, 0, 0), null, 26);
var placement = new WorldParticleScriptPlacement(123, creation, definition, 0, 0);
var catalogue = SacredParticleCatalogue.LoadEmbedded();
var emitter = new WorldParticleEmitter(placement, catalogue.WorldUnitsPerTile);
var output = new List<WorldParticle>();
var checks = 0;
void Near(float actual, float expected, string label)
{
    if (Math.Abs(actual - expected) > 0.0001f) throw new Exception($"{label}: {actual} != {expected}");
    checks++;
}
void Verify(WorldParticle particle, float[] expected)
{
    // Independent reference viewport/view equations, not the production projection helper.
    var ground = IsometricProjection.WorldToIso(particle.WorldX, particle.WorldY);
    Near(ground.X, expected[0] * 1024 / 534, "native horizontal offset/motion");
    Near(ground.Y, expected[1] / MathF.Sqrt(5) * 768 / 400, "native ground offset/motion");
    Near(particle.Height, (26 + expected[2]) * 2 / MathF.Sqrt(5) * 768 / 400, "script and spawn height");
    Near(particle.Size, 2 * expected[10] * 1024 / 534, "native quad width");
    Near(particle.Rotation, expected[12], "native angular motion");
    Near(particle.Color & 255, (int)expected[11], "color follows truncated remaining fade");
}
emitter.Update(0.125f, output);
Verify(output.Single(), initial);
foreach (var step in reference.RootElement.GetProperty("steps").EnumerateArray())
{
    output.Clear();
    emitter.Update(step.GetProperty("dt").GetSingle(), output);
    Verify(output.Single(), step.GetProperty("state").EnumerateArray().Select(x => x.GetSingle()).ToArray());
}

// A half-range must reach both sides of +/- 0.5*value; the previous implementation cannot.
Array.Clear(emission); Write(8, 1); Write(0x44, 10); Write(0x58, 0.001f);
var scatterSet = set with { Emission = MemoryMarshal.Read<SacredParticleEmissionLayout>(emission), Motion = default };
var scatter = new WorldParticleEmitter(placement with { Definition = definition with { ParameterSets = [scatterSet] } }, catalogue.WorldUnitsPerTile);
output.Clear(); scatter.Update(0.1f, output);
var offsets = output.Select(p => IsometricProjection.WorldToIso(p.WorldX, p.WorldY).X / catalogue.Projection.HorizontalScale).ToArray();
if (offsets.Min() < -10 || offsets.Max() > 10 || offsets.Min() >= -5 || offsets.Max() <= 5)
    throw new Exception("Spawn variation does not span the native half-range.");
checks++;
Console.WriteLine($"Passed {checks} runtime checks against the native integrator, projection and random half-range.");
