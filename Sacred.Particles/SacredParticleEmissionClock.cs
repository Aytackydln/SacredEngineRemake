namespace Sacred.Particles;

/// <summary>Which native elapsed-time value is passed to stdCreation and tested against the emission limit.</summary>
public enum SacredParticleEmissionClock
{
    /// <summary>Advance age first; the previous age permits the crossing update (Windstrike/Burning Bone).</summary>
    CrossingUpdate,
    /// <summary>Emit at the previous age while it is strictly below the limit, then advance age.</summary>
    PreviousTimeExclusive,
    /// <summary>Advance age first, then emit only while the new age is strictly below the limit.</summary>
    CurrentTimeExclusive,
    /// <summary>Advance age first, then emit through the equality boundary (Nature Healing).</summary>
    CurrentTimeInclusive
}
