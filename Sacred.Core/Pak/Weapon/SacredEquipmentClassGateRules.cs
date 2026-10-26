namespace Sacred.Core.Pak.Weapon;

/// <summary>Original selector's two default-variant probes and resource-name gate.</summary>
public readonly record struct SacredEquipmentClassGateRules(uint FirstSource, uint SecondSource,
    uint MinimumReplacementSource, string NameMarker);
