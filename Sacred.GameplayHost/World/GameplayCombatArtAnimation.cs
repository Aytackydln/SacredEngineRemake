namespace Sacred.GameplayHost.World;

/// <summary>Arguments to native animate before creature, authored and global multipliers.</summary>
public readonly record struct GameplayCombatArtAnimation(uint Motion, float Speed, float BlendNumerator = .5f)
{
    public GameplayCombatArtRateSource RateSource { get; init; }
    public uint? FallbackMotion { get; init; }
}

public enum GameplayCombatArtRateSource { Fixed, AttackSpeed, CastSpeed }
