using System.Globalization;
using System.Text;
using Sacred.GameServer.Data;

namespace Sacred.GameServer.Demo;

/// <summary>A made-up world map for <c>--demo</c> without --game-dir: a green island of about 1800 sectors on a 100 x 100 cell grid in the game's
/// own map projection, so the sector pages look like the real thing. Sector ids count up from 1000.</summary>
public static class DemoMap
{
    public const int Width = 2048;

    public static (byte[] Json, byte[] Png) Build()
    {
        var sectors = new List<(int Id, int X, int Y)>();
        var next = 1000;
        for (var y = 0; y < 100; y++)
            for (var x = 0; x < 100; x++)
                if (Land(x, y)) sectors.Add((next++, x, y));
        var land = new HashSet<(int, int)>(sectors.Select(s => (s.X, s.Y)));
        var p = AreaMap.ProjectionFor(Width);
        var rgba = new byte[Width * Width * 4];
        for (var py = 0; py < Width; py++)
            for (var px = 0; px < Width; px++)
            {
                var u = (px - p.Ox) / p.Kx; var v = (py - p.Oy) / p.Ky;
                double tx = (u + v) / 2, ty = (v - u) / 2;
                var at = (py * Width + px) * 4;
                if (land.Contains(((int)Math.Floor(tx / 64), (int)Math.Floor(ty / 64))))
                {
                    var h = (Math.Sin(tx * .02) + Math.Cos(ty * .023) + Math.Sin((tx + ty) * .011)) / 3;
                    rgba[at] = (byte)(70 + 40 * h); rgba[at + 1] = (byte)(105 + 45 * h); rgba[at + 2] = 58;
                }
                else { rgba[at] = 24; rgba[at + 1] = 42; rgba[at + 2] = 64; }
                rgba[at + 3] = 255;
            }
        var inv = CultureInfo.InvariantCulture;
        var json = new StringBuilder();
        json.Append(inv, $"{{\"available\":true,\"width\":{Width},\"height\":{Width},\"projection\":{{\"ox\":{p.Ox},\"oy\":{p.Oy},\"kx\":{p.Kx},\"ky\":{p.Ky}}},\"sectors\":[");
        json.Append(string.Join(",", sectors.Select(s => $"{{\"id\":{s.Id},\"x\":{s.X},\"y\":{s.Y}}}"))).Append("]}");
        return (Encoding.UTF8.GetBytes(json.ToString()), AreaMap.EncodePng(Width, Width, rgba));
    }

    private static bool Land(int x, int y)
    {
        double dx = (x - 46) / 26.0, dy = (y - 46) / 22.0;
        return dx * dx + dy * dy + 0.15 * Math.Sin(x * .3) * Math.Cos(y * .27) < 1;
    }
}
