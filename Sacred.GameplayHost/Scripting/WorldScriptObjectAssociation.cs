namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x486CB2..0x486D51 gates. Resolution and creature RTTI follow
/// this predicate; a found non-creature still takes ordinary construction.</summary>
public static class WorldScriptObjectAssociation
{
    public static bool ShouldLookup(string? secondaryText, uint primaryResource,
        uint secondaryResource, uint interpreterFlags) =>
        !string.IsNullOrEmpty(secondaryText) &&
        (unchecked((int)secondaryResource) > 0 || unchecked((int)primaryResource) < -1) &&
        (interpreterFlags & 1) != 0;

    /// <summary>The original caller requests ownership before appending actions.
    /// This predicate does not perform or acknowledge that transfer.</summary>
    public static bool RequiresOwnershipRequest(bool networking, uint reference,
        byte objectOwner, uint controllerOwner) => networking && objectOwner != controllerOwner && reference > 16;
}
