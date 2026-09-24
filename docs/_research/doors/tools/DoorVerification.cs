using System.Numerics;
using Sacred.Assets.Paks.Models;
using Sacred.World.Objects;
using Sacred.Granny.Animation;

internal static class DoorVerification
{
    public static async Task<object> Run(ModelsPakArchive models, string name)
    {
        var asset = await models.LoadModelAsync(name);
        var mesh = asset.Mesh ?? throw new InvalidOperationException($"Missing mesh: {name}");
        var skin = asset.Skin ?? throw new InvalidOperationException($"Missing skin: {name}");
        var open = await models.LoadModelAnimationAsync(name, 0xAA) ?? throw new InvalidOperationException($"Missing activation: {name}");
        var close = await models.LoadModelAnimationAsync(name, 0xAB) ?? throw new InvalidOperationException($"Missing deactivation: {name}");
        var source = mesh.Vertices.Select(v => v.Position).ToArray();
        var playback = new DoorMotionPlayback(mesh, skin, open, close);
        playback.SetState(true);
        var start = playback.Mesh.Vertices.Select(v => v.Position).ToArray();
        var bindError = MaximumDistance(source, start);
        playback.Update(open.DurationSeconds + 1);
        var end = playback.Mesh.Vertices.Select(v => v.Position).ToArray();
        var moving = start.Zip(end).Count(p => Vector3.Distance(p.First, p.Second) > .001f);
        if (moving == 0) throw new InvalidOperationException($"No moving geometry: {name}");
        playback.Update(100);
        if (MaximumDistance(end, playback.Mesh.Vertices.Select(v => v.Position).ToArray()) > .00001f)
            throw new InvalidOperationException($"Endpoint wrapped: {name}");
        var reloaded = new DoorMotionPlayback(mesh, skin, open, close);
        reloaded.SetInitialState(true);
        if (MaximumDistance(end, reloaded.Mesh.Vertices.Select(v => v.Position).ToArray()) > .00001f)
            throw new InvalidOperationException($"Reload pose differs: {name}");
        playback.SetState(false);
        var reverseJoinError = MaximumDistance(end, playback.Mesh.Vertices.Select(v => v.Position).ToArray());
        playback.Update(close.DurationSeconds + 1);
        var closed = playback.Mesh.Vertices.Select(v => v.Position).ToArray();
        var roundTripError = MaximumDistance(start, closed);
        // Authored endpoint keys differ slightly; a mirrored hinge used to jump tens
        // of model units. These tolerances allow the measured sub-two-unit key drift.
        if (bindError > 1 || reverseJoinError > 1 || roundTripError > 2)
            throw new InvalidOperationException($"Discontinuous authored poses: {name}");
        if (MaximumDistance(source, mesh.Vertices.Select(v => v.Position).ToArray()) != 0)
            throw new InvalidOperationException($"Source mesh mutated: {name}");
        if (start.Concat(end).Concat(closed).Any(p => !float.IsFinite(p.X + p.Y + p.Z)))
            throw new InvalidOperationException($"Non-finite pose: {name}");
        Console.WriteLine($"Verified {name}: moving {moving}/{source.Length}, bind delta {bindError:F4}, close join {reverseJoinError:F4}, round trip {roundTripError:F4}");
        return new { name, vertices = source.Length, moving, bindError, reverseJoinError, roundTripError };
    }

    private static float MaximumDistance(Vector3[] a, Vector3[] b) => a.Zip(b).Max(p => Vector3.Distance(p.First, p.Second));
}
