using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

public readonly record struct SacredScriptPopulationType(uint Type, int Weight, int Group);

/// <summary>Literal FillRegion inputs. Reading the list does not place objects.</summary>
public sealed record SacredScriptPopulation(IReadOnlyList<SacredScriptPopulationType> Types,
    int MinimumCount, int MaximumCount, int AreaDivisor, int CreatureValue,
    int? SectorX, int? SectorY, int MinimumLevel, int MaximumLevel)
{
    public const ushort Opcode = 51;
}

public static class SacredScriptPopulationReader
{
    public static SacredScriptPopulation Read(SacredScriptCommand command)
    {
        if (command.Opcode != SacredScriptPopulation.Opcode) throw new ArgumentException("Expected FillRegion.", nameof(command));
        var types = new List<SacredScriptPopulationType>();
        int minimum = 1, maximum = 1, divisor = 100, value = 0, minLevel = -1, maxLevel = 0;
        int? x = null, y = null;
        foreach (var operand in SacredScriptOperandReader.Read(command))
            switch (operand.Kind)
            {
                case SacredScriptArgumentKind.TypeId:
                    types.Add(new(unchecked((uint)operand.Integer!.Value), 100, 0)); break;
                case SacredScriptArgumentKind.PopulationWeightedType:
                    types.Add(new(unchecked((uint)operand.Integer!.Value), operand.SecondInteger!.Value, operand.ThirdInteger!.Value)); break;
                case SacredScriptArgumentKind.PopulationDensity:
                    minimum = Math.Min(operand.Integer!.Value, operand.SecondInteger!.Value);
                    maximum = Math.Max(operand.Integer.Value, operand.SecondInteger.Value);
                    divisor = operand.ThirdInteger!.Value; break;
                case SacredScriptArgumentKind.PopulationCreatureValue: value = operand.Integer!.Value; break;
                case SacredScriptArgumentKind.PopulationSector: x = operand.Integer; y = operand.SecondInteger; break;
                case SacredScriptArgumentKind.PopulationLevels: minLevel = operand.Integer!.Value; maxLevel = operand.SecondInteger!.Value; break;
                default: throw new InvalidDataException($"FillRegion effect 0x{(byte)operand.Kind:X2} requires native population handling.");
            }
        if (types.Count > 100) throw new InvalidDataException("FillRegion exceeds the original local 100-entry list.");
        return new(types.AsReadOnly(), minimum, maximum, divisor, value, x, y, minLevel, maxLevel);
    }
}
