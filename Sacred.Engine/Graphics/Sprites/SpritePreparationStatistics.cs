using System;
using System.Diagnostics;

namespace Sacred.Engine.Graphics.Sprites;

internal sealed class SpritePreparationStatistics
{
    private double _textures;
    private double _instances;
    private long _frames;

    public void RecordTextures(long started) => _textures += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    public void RecordInstances(long started)
    {
        _instances += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _frames++;
    }
    public string Status => $"CPU sprite preparation {(_textures + _instances) / Math.Max(1, _frames):F4} ms/frame " +
        $"(textures {_textures / Math.Max(1, _frames):F4}, instances {_instances / Math.Max(1, _frames):F4}; {_frames} frames)";
    public void Reset() { _textures = 0; _instances = 0; _frames = 0; }
}
