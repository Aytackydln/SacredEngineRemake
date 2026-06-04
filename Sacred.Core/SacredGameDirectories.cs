namespace Sacred.Core;

public class SacredGameDirectories
{
    public required string ReferenceResourcesPath { get; init; }
    
    public required string GlobalResourcesPath { get; init; }
    public required string LocalResourcesPath { get; init; }
    
    public required string ItemsPakPath { get; init; }
    public required string WeaponsPakPath { get; init; }
    public required string TexturesPakPath { get; init; }
}