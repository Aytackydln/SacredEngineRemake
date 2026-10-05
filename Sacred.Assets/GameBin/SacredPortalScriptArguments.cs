using System.Buffers.Binary;
using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Literal subset used by portal startup declarations and Teleport callbacks.
/// Unknown operands reject the instruction rather than searching its bytes for a tag.</summary>
public sealed class SacredPortalScriptArguments
{
    public string? Name { get; private set; }
    public List<int> Integers { get; } = [];
    public Dictionary<byte, SacredScriptPosition> Positions { get; } = [];

    public static SacredPortalScriptArguments? Read(SacredScriptCommand command,
        IReadOnlyDictionary<string, SacredScriptPosition> definitions)
    {
        var result = new SacredPortalScriptArguments();
        var data = command.Bytes.Span;
        if (data.Length < SacredScriptCommandHeaderLayout.SerializedSize ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != data.Length) return null;
        var offset = 4;
        while (offset < data.Length)
        {
            var kind = (SacredScriptArgumentKind)data[offset++];
            if (kind == SacredScriptArgumentKind.NullTerminatedString)
            {
                var end = data[offset..].IndexOf((byte)0);
                if (end < 0) return null;
                result.Name = Encoding.Latin1.GetString(data.Slice(offset, end));
                offset += end + 1;
            }
            else if (kind == SacredScriptArgumentKind.Integer)
            {
                if (data.Length - offset < 4) return null;
                result.Integers.Add(BinaryPrimitives.ReadInt32LittleEndian(data[offset..]));
                offset += 4;
            }
            else if (kind is SacredScriptArgumentKind.TilePosition or
                     SacredScriptArgumentKind.TriggerFirstCorner or SacredScriptArgumentKind.TriggerLastCorner)
            {
                if (data.Length - offset < 4) return null;
                var x = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
                offset += 4;
                SacredScriptPosition position;
                if (x == -2)
                {
                    var end = data[offset..].IndexOf((byte)0);
                    if (end < 0) return null;
                    var name = Encoding.Latin1.GetString(data.Slice(offset, end));
                    if (!definitions.TryGetValue(name, out position)) return null;
                    offset += end + 1;
                }
                else
                {
                    if (data.Length - offset < 8) return null;
                    position = new(x, BinaryPrimitives.ReadInt32LittleEndian(data[offset..]),
                        BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 4)..]));
                    offset += 8;
                }
                result.Positions[(byte)kind] = position;
            }
            else return null;
        }
        return result;
    }
}
