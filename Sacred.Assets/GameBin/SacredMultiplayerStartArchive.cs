using System.Runtime.InteropServices;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Original fixed table read by Gold 460CF4..460D2E. Retains native
/// zero initialization and prefix-read semantics; does not resolve a world sector.</summary>
public sealed class SacredMultiplayerStartArchive
{
    private readonly byte[] _data;
    private SacredMultiplayerStartArchive(byte[] data) => _data = data;
    public ReadOnlyMemory<byte> Data => _data;

    public static SacredMultiplayerStartArchive Load(string installation)
    {
        var path = Path.Combine(installation, "bin", "MultiStart.bin");
        // Native fopen failure leaves the constructor's cleared table untouched.
        try
        {
            using var stream = File.OpenRead(path);
            var bytes = new byte[SacredMultiplayerStartPositionLayout.FileSize];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = stream.Read(bytes.AsSpan(count));
                if (read == 0) break;
                count += read;
            }
            return new(bytes);
        }
        catch (FileNotFoundException) { return FromPayload([]); }
        catch (DirectoryNotFoundException) { return FromPayload([]); }
    }

    public static SacredMultiplayerStartArchive FromPayload(ReadOnlySpan<byte> data)
    {
        var bytes = new byte[SacredMultiplayerStartPositionLayout.FileSize];
        data[..Math.Min(bytes.Length, data.Length)].CopyTo(bytes);
        return new(bytes);
    }

    /// <summary>Gold 4655D0 active multiplayer path. The caller supplies the
    /// compared connection/campaign predicate and native level-mode word.</summary>
    public SacredMultiplayerStartPositionLayout Select(byte slot, bool campaign, ushort levelMode)
    {
        if (slot is < 1 or > SacredMultiplayerStartPositionLayout.SlotsPerTable)
            throw new ArgumentOutOfRangeException(nameof(slot));
        var row = slot - 1 + (campaign && levelMode == 0 ? SacredMultiplayerStartPositionLayout.SlotsPerTable : 0);
        return MemoryMarshal.Read<SacredMultiplayerStartPositionLayout>(_data.AsSpan(row * SacredMultiplayerStartPositionLayout.SerializedSize));
    }
}
