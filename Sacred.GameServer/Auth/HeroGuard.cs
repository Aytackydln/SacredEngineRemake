using System.Buffers.Binary;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Auth;

/// <summary>
/// Plausibility of a player's own hero state (0x169/0x16B/0x16C/0x16E/0x16F whose reference is the sender's slot). Hero values
/// (Gold 0x585C60 stream at body +445, marker 0xDEADBEEF): the compact form has the level word at +52, the full form (0x16B) the
/// sCharValHero block at +28 with the level at +78. A hero above <see cref="AuthoritativeOptions.MaxHeroLevel"/> cannot have
/// been played up legitimately.
/// </summary>
public static class HeroGuard
{
    /// <summary>The hero level of a creature body, or -1 when it carries no hero values.</summary>
    public static int Level(ReadOnlySpan<byte> body, bool full)
    {
        var values = GoldCreatureStateLayout.PrefixSize;
        if (body.Length < values + GoldCreatureValuesLayout.CompactMinimumSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(body[GoldCreatureStateLayout.MarkerOffset..]) != GoldCreatureStateLayout.Marker ||
            BinaryPrimitives.ReadUInt32LittleEndian(body[values..]) != GoldCreatureValuesLayout.Marker) return -1;
        if (!full) return BinaryPrimitives.ReadUInt16LittleEndian(body[(values + GoldCreatureValuesLayout.CompactWord56Offset)..]);
        if (body.Length < values + GoldCreatureValuesLayout.FullMinimumSize) return -1;
        return BinaryPrimitives.ReadUInt16LittleEndian(body[(values + GoldCreatureValuesLayout.FullBlock08Offset + 78)..]);
    }

    /// <summary>Null when the hero state is plausible, else the reason.</summary>
    public static string? Check(ReadOnlySpan<byte> body, bool full, AuthoritativeOptions options)
    {
        if (options.MaxHeroLevel <= 0) return null;
        var level = Level(body, full);
        return level > options.MaxHeroLevel ? $"hero level {level} above the cap {options.MaxHeroLevel}" : null;
    }
}
