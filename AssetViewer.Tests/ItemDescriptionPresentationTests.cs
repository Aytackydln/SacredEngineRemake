using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.Pak.Items;
using Sacred.Core.UI;
using Sacred.UI.Inventory;

namespace AssetViewer.Tests;

[TestClass]
public sealed class ItemDescriptionPresentationTests
{
    [TestMethod]
    public void FrameMatchesEightNativeQuadsIncludingReflectedCorners()
    {
        // Original Gold atlas records; expected quads were independently captured by
        // executing pureHD cUI_Window2::adjustFrame at 0x727620 for a 350x200 window.
        var table = new SacredExecutableUiTextureDefinitionLayout[56];
        table[21] = Record("GUI_main_03.TGA", 224, 0, 255, 5);
        table[34] = Record("GUI_main_03.TGA", 120, 112, 125, 143);
        table[55] = Record("GUI_main_02.TGA", 114, 180, 151, 197);
        var actual = new ItemDescriptionFrame(table).GetQuads(350, 200).ToArray();
        Vector4[] rectangles = [new(4, -4, 346, 6), new(4, 197, 346, 6), new(-4, 0, 6, 200), new(350, 0, 6, 200),
            new(326, -11, 38, 18), new(-13, -11, 38, 18), new(326, 192, 38, 18), new(-13, 192, 38, 18)];
        Vector4[] uvPixels = [new(224, 0, 255, 6), new(224, 6, 255, 0), new(120, 112, 126, 143), new(120, 112, 126, 143),
            new(114, 180, 152, 198), new(151, 180, 113, 198), new(114, 198, 152, 180), new(151, 198, 113, 180)];
        Assert.AreEqual(8, actual.Length);
        for (var index = 0; index < actual.Length; index++)
        {
            var quad = actual[index].Quad;
            Assert.AreEqual(rectangles[index], new Vector4(quad.Position, quad.Size.X, quad.Size.Y), $"Quad {index} rectangle");
            Assert.AreEqual(uvPixels[index] / 256, new Vector4(quad.Piece.UvMinimum.X, quad.Piece.UvMinimum.Y,
                quad.Piece.UvMaximum.X, quad.Piece.UvMaximum.Y), $"Quad {index} UVs");
        }
    }

    [TestMethod]
    public void NameColorsFollowNativeBoundariesAndSetOrClassPrecedence()
    {
        for (uint rarity = 0; rarity < 16; rarity++)
        {
            uint expected = rarity <= 4 ? 0xFFEFEFEF : rarity < 12 ? 0xFFA0A0FF : rarity < 15 ? 0xFFF0E040 : 0xFFC19A33;
            Assert.AreEqual(expected, ItemDescriptionColors.Name(0xC0003F | rarity << 16, 0, SacredItemCategory.Weapon));
        }
        Assert.AreEqual(0xFF15FF25u, ItemDescriptionColors.Name(1, 1, SacredItemCategory.Weapon));
        Assert.AreEqual(0xFF87CEFAu, ItemDescriptionColors.Name(1, 0, SacredItemCategory.Weapon));
        Assert.AreEqual(0xFFFF6C00u, ItemDescriptionColors.Name(0x800001, 0, SacredItemCategory.Weapon));
    }

    private static SacredExecutableUiTextureDefinitionLayout Record(string name, float u0, float v0, float u1, float v1)
    {
        var bytes = new byte[84];
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(bytes, 8);
        MemoryMarshal.Write(bytes.AsSpan(44), in u0); MemoryMarshal.Write(bytes.AsSpan(48), in v0);
        MemoryMarshal.Write(bytes.AsSpan(52), in u1); MemoryMarshal.Write(bytes.AsSpan(56), in v1);
        return MemoryMarshal.Read<SacredExecutableUiTextureDefinitionLayout>(bytes);
    }
}
