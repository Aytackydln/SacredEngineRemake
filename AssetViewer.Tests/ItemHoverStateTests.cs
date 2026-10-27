using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Inventory.Actors;
using Sacred.UI.Inventory;

namespace AssetViewer.Tests;

[TestClass]
public sealed class ItemHoverStateTests
{
    [TestMethod]
    public void ClosingSwitchingTabsAndRebindingRemoveStaleHover()
    {
        var state = new EquipmentUiState();
        state.Bind(new SacredGameActor(SacredCharacterClass.Seraphim));
        state.SetOpen(true); state.Select(0);
        Assert.IsNull(state.ItemHover.Slot, "Selection alone does not open a hover card.");
        state.ItemHover.Update(0, new(20, 30));
        Assert.AreEqual(0, state.ItemHover.Slot);
        state.SetOpen(false); Assert.IsNull(state.ItemHover.Slot);
        state.SetOpen(true); state.ItemHover.Override(0);
        state.ItemHover.Update(null, default);
        Assert.AreEqual(0, state.ItemHover.Slot, "A debug target survives ordinary pointer updates.");
        state.SetTab(InventoryTab.CombatArts); Assert.IsNull(state.ItemHover.Slot);
        state.ItemHover.Override(0); state.Bind(new SacredGameActor(SacredCharacterClass.Gladiator));
        Assert.IsNull(state.ItemHover.Slot);
        state.ItemHover.Update(1, default); state.ClearActor(); Assert.IsNull(state.ItemHover.Slot);
    }

    [TestMethod]
    public void PopupFitsEveryViewportEdgeAndScalesAnOversizedDescription()
    {
        foreach (var viewport in new[] { new Vector2(800, 600), new Vector2(1920, 1080) })
        foreach (var anchor in new[] { Vector2.Zero, viewport, new Vector2(viewport.X, 0), new Vector2(0, viewport.Y) })
        foreach (var native in new[] { new Vector2(400, 300), new Vector2(500, 1800) })
        {
            var (position, scale) = ItemDescriptionPopupLayout.Place(anchor, native, viewport, 1.5f);
            Assert.IsTrue(position.X >= 8 && position.Y >= 8);
            Assert.IsTrue(position.X + native.X * scale <= viewport.X - 7.99f);
            Assert.IsTrue(position.Y + native.Y * scale <= viewport.Y - 7.99f);
        }
        Assert.AreEqual(0xFF449BEAu, ItemDescriptionPopupLayout.ToAbgr(0xFFEA9B44));
    }
}
