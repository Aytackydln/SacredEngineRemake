using System.Buffers.Binary;
using System.Globalization;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.GameRes;
using Sacred.Core.World;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold SetRG producer and its private bank-1 section-5 table.
/// Shares the independently compared native table mutations with receipt.
/// Sound-name resolution and symbolic/random positions remain guarded.</summary>
public sealed class WorldScriptRegions(SacredSectorGrid grid, GameResStore? resources, byte owner = 1)
{
    private readonly GoldScriptRegionReplica _state = new();
    private readonly List<GoldMessage> _publications = [];
    public IReadOnlyDictionary<uint,IReadOnlyList<GoldScriptRegionRecord>> Records => _state.Regions;
    public IReadOnlyList<GoldMessage> Publications => _publications.AsReadOnly();

    /// <summary>Native bank-1 section 5: ascending AVL keys, each followed by
    /// its count and ordered 32-byte records. Count belongs in bank header word 5.
    /// Other bank sections and mandatory globals must be established separately.</summary>
    public (uint Count, byte[] Bytes) WriteBankSection()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var group in Records.OrderBy(pair=>pair.Key))
        {
            writer.Write(group.Key);
            writer.Write(checked((uint)group.Value.Count));
            foreach (var record in group.Value) writer.Write(record.ToPayload());
        }
        return (checked((uint)Records.Count),stream.ToArray());
    }

    public void Apply(SacredScriptCommand command, bool startup, bool multiplayer = true)
    {
        if (command.Opcode is not (SacredScriptRegionLayout.SetOpcode or SacredScriptRegionLayout.RemoveOpcode))
            throw new ArgumentException("Expected SetRG/UnSetRG.",nameof(command));
        var coordinates = new int[SacredScriptRegionLayout.CoordinateCount];
        var count = 0;
        uint resource = 0;
        var text = "";
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.Integer && operand.Integer is { } number)
            {
                if (count < coordinates.Length) coordinates[count] = number;
                count++;
            }
            else if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString)
            {
                text = operand.Text!;
                resource = 0;
                if (text.Contains('$') || text.Contains("+Var(",StringComparison.Ordinal))
                    throw new NotSupportedException("SetRG expressions require original name substitution.");
                if (text.StartsWith("Res:",StringComparison.OrdinalIgnoreCase))
                {
                    var original = resources ?? throw new InvalidDataException("SetRG requires original resource text.");
                    resource = WorldScriptResourceReference.Resolve(text,original);
                    var key = (resource & 0x80000000) != 0 ? resource & 0x7fffffff :
                        SacredResourceHash.ComputeDefaultCLocale(System.Text.Encoding.Latin1.GetBytes(resource.ToString(CultureInfo.InvariantCulture)));
                    if (original.Strings.TryGetValue(key,out var resolved)) text = resolved;
                }
                // Each parsed name replaces both text and resource identity.
                if (text.Length >= SacredScriptRegionLayout.NameCapacity) text = text[..(SacredScriptRegionLayout.NameCapacity-1)];
            }
            else throw new NotSupportedException($"SetRG operand 0x{(byte)operand.Kind:X2} requires its native position/variable parser.");
        }
        if (count != coordinates.Length || text.Length == 0) return;
        if (resource != SacredScriptRegionLayout.SpecialRegionResource && text.Contains("SOUND_",StringComparison.Ordinal))
            throw new NotSupportedException("SetRG SOUND_ names require the original sound resolver.");
        var first = grid.GetSector(coordinates[0],coordinates[1]);
        var second = grid.GetSector(coordinates[2],coordinates[3]);
        var bytes = new byte[GoldScriptRegionCodec.MinimumSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes,command.Opcode == SacredScriptRegionLayout.SetOpcode ? 1u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4),first);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8),second);
        for (var axis=0;axis<2;axis++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12+axis*4),Math.Min(coordinates[axis],coordinates[axis+2]));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20+axis*4),Math.Max(coordinates[axis],coordinates[axis+2]));
        }
        var special = resource == SacredScriptRegionLayout.SpecialRegionResource;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28),special ? 0 : resource);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32),special ? 1u : 0);
        var message = new GoldMessage(GoldScriptRegionCodec.Opcode,owner,owner,0,bytes);
        _state.Apply(GoldScriptRegionCodec.Decode(message));
        if (multiplayer && !startup) _publications.Add(message);
    }
}
