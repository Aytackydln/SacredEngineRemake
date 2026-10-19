using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Resolved inputs at an unimplemented CreateNPC factory boundary. This is
/// diagnostic pending work: no actor/reference allocation or execution credit is implied.</summary>
public sealed record WorldParticipantCreatureRequest(int SourceOffset, SacredScriptCreateCreature Creation,
    GoldObjectPosition? Placement)
{
    /// <summary>Current received name lookup only; an absent value requires native name reservation.</summary>
    public uint? NamedResource { get; init; }
    public uint? ExistingObjectReference { get; init; }
    public ushort FacingDegrees { get; init; }
    /// <summary>Resolved from the host's current regional table, before native factory normalization.
    /// Authored script inputs above remain unchanged.</summary>
    public SacredScriptCreatureReplacement? RegionalReplacement { get; init; }
    public uint EffectiveTypeId => RegionalReplacement?.TypeId ?? Creation.TypeId;
    public uint EffectiveBehaviorFlags => RegionalReplacement?.BehaviorFlags ?? Creation.Behavior;
}
