using System.Buffers.Binary;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.GameRes;
using Sacred.Core.World;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x4A81F0/0x499F80 startup map-icon producer. Retains ordered
/// 24-byte records privately; interpreter +0x14 suppresses individual sends.</summary>
public sealed class WorldScriptMapIcons
{
    private readonly List<GoldMapIconState> _records = [];
    private readonly SacredSectorGrid _grid;
    private readonly GameResStore? _resources;
    public WorldScriptMapIcons(SacredSectorGrid grid, GameResStore? resources)
    {
        (_grid, _resources) = (grid, resources);
        Records = _records.AsReadOnly();
    }
    public IReadOnlyList<GoldMapIconState> Records { get; }

    public void Apply(SacredScriptCommand command, bool startup)
    {
        if (!startup) throw new InvalidDataException("Live SetMapIcon requires native insertion/publication lifecycle.");
        if (command.Opcode != SacredScriptPortalOpcodes.SetMapIcon) throw new ArgumentException("Expected SetMapIcon.", nameof(command));
        var x = -1; var y = -1; var third = -1001; var fourth = -1001;
        uint resource = 0;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.Integer)
            {
                var value = operand.Integer!.Value;
                if (x < 0) x = value;
                else if (y < 0) y = value;
                else if (third < -1000) third = value;
                else if (fourth < -1000) fourth = value;
            }
            else if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString)
                resource = WorldScriptResourceReference.Resolve(operand.Text!, _resources ??
                    throw new InvalidDataException("SetMapIcon requires original resource lookup."));
            else throw new InvalidDataException($"SetMapIcon operand 0x{(byte)operand.Kind:X2} requires native decoding.");
        }
        if (x < 0 || y < 0 || third < -1001) return;
        // Original sMapIcon alignment holes are padding, not script fields.
        var bytes = new byte[GoldScriptSpatialLayout.Size];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, unchecked((ushort)_grid.GetSector(x, y)));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(GoldScriptSpatialLayout.XOffset), x);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(GoldScriptSpatialLayout.YOffset), y);
        bytes[GoldScriptSpatialLayout.KindOrCurvatureOffset] = unchecked((byte)third);
        if (fourth >= -1001 && resource is not (0 or uint.MaxValue))
        {
            bytes[GoldScriptSpatialLayout.LabelAngleOffset] = unchecked((byte)fourth);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldScriptSpatialLayout.TextResourceOffset), resource);
        }
        _records.Add(GoldMapIconState.FromPayload(bytes));
    }
}
