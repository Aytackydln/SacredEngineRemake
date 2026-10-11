namespace Sacred.Particles;

/// <summary>The model and its ordinary material resolve through the same Items.pak row.</summary>
public sealed record SacredStonedFxDefinition(uint ItemModelTypeId, string OwnerTexture, string AuraTexture)
{
    public float DefaultDuration { get; init; }
    public float RandomScale { get; init; }
    public float FullTurn { get; init; }
    public uint StartNotification { get; init; }
    public uint OwnerTextureOperation { get; init; }
    public uint OwnerTextureMask { get; init; }
}
