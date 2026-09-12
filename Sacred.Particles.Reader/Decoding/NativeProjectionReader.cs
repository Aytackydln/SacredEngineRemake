using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeProjectionReader
{
    public static SacredParticleProjection Read(SacredExecutableImage image)
    {
        // Immediate operands of the reference ortho call at 0x6282C9..0x6282EC.
        var left = image.Single(0x6282E3);
        var right = image.Single(0x6282DE);
        var bottom = image.Single(0x6282D9);
        var top = image.Single(0x6282D4);
        // Default camera eye (0,1200,600), target (0,0,0), up (0,0,1).
        var eyeY = image.Single(0x811710);
        var eyeZ = image.Single(0x81171A);
        var distance = MathF.Sqrt(eyeY * eyeY + eyeZ * eyeZ);
        // Reciprocal reference viewport dimensions used by precise position conversion.
        return new SacredParticleProjection(
            1 / image.Single(0x890710) / (right - left),
            1 / image.Single(0x89205C) / (top - bottom),
            eyeZ / distance, eyeY / distance,
            image.Single(0x418F83)); // Environment constructor: wind along +X, strength +0x74.
    }
}
