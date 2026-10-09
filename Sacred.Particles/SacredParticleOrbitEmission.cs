namespace Sacred.Particles;

/// <summary>Native reanimate updater rotates the emission origin before creation.
/// Its accumulated float angle, rather than an elapsed-time division, stops births.</summary>
public sealed record SacredParticleOrbitEmission(float Radius, float AngularSpeed, float LimitAngle,
    uint NativeAddress);
