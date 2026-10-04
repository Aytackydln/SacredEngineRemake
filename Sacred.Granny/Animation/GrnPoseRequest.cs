namespace Sacred.Granny.Animation;

/// <summary>Identity is never reused; generation changes explicitly invalidate deferred requests.</summary>
public readonly record struct GrnPoseInstanceHandle(long Identity, uint Generation);
public enum GrnPoseTimeMode { Loop, Clamp }

/// <summary>A playback command, independent of evaluated CPU palettes or vertices.
/// Clip objects are shared authored sources and must not be mutated after publication.</summary>
public readonly record struct GrnPoseRequest(
    GrnPoseInstanceHandle Instance, ulong Sequence, ulong SourceVersion,
    GrnAnimationClip Animation, float TimeSeconds, GrnPoseTimeMode TimeMode,
    bool MaterializeCpuVertices);

public interface IGrnPoseRequestSink
{
    void Submit(in GrnPoseRequest request);
}
