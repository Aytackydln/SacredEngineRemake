using Sacred.GameServer.Session;

namespace Sacred.GameServer.Auth;

/// <summary>
/// Turns the server's current state of a player's hero (object world, inventories) into the lobby's character block
/// (556-byte preview, u32 zlib size, u32 size, zlib hero file). The server has no real hero objects yet, so the default
/// implementation is absent and saves are skipped; the object-world stage plugs in here.
/// </summary>
public interface IHeroSerializer
{
    /// <summary>Writes the block into <paramref name="destination"/> and returns its length, or 0 when there is nothing to save.</summary>
    int Serialize(PlayerSession player, Span<byte> destination);
}
