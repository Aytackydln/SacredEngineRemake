namespace Sacred.Core.World;

/// <summary>
/// Native cWorldView::eWorldViewLayers queue order. These are runtime queue
/// indices, not an interpretation of Items.pak +0x2F or Static.pak +0x33.
/// Preserve diagonal tile traversal and linked-list order within each queue.
/// </summary>
public enum WorldRenderLayer
{
    Floor = 0,
    Shadows = 1,
    Floor2 = 2,
    Objects = 3,
    Ceiling = 4,
}
