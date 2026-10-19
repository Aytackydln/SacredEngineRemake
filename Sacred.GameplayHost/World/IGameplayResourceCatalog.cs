using Sacred.Core.Pak.Items;

namespace Sacred.GameplayHost.World;

/// <summary>Original item definitions supplied by the installation's resource loader.</summary>
public interface IGameplayResourceCatalog
{
    ItemsPakEntry? GetItem(uint typeId);
}
