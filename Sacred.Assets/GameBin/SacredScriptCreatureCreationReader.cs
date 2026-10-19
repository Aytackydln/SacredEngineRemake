using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Verified CreateNPC operand subset. Decoding does not allocate actors, equipment or
/// callbacks, and cannot stand in for the native creature constructor and level/value calculation.</summary>
public static class SacredScriptCreatureCreationReader
{
    public static SacredScriptCreateCreature Read(SacredScriptCommand command, Func<string, SacredScriptPosition> resolvePosition,
        Func<string, int?>? resolveVariable = null)
    {
        ArgumentNullException.ThrowIfNull(resolvePosition);
        if (command.Opcode != SacredScriptCreateCreature.Opcode) throw new ArgumentException("Expected CreateNPC.", nameof(command));
        string? name = null;
        uint type = 0, behavior = 0;
        var equipment = new List<uint>();
        SacredScriptPosition? position = null;
        SacredScriptPosition? worldPosition = null;
        var placement = new SacredScriptPosition(-1, 0, -1);
        ushort? facing = null;
        var group = 0;
        var level = 0;
        var aggression = -1;
        var explicitBehavior = false;
        var flag2B7Bit8 = -1;
        uint constructionFlags = 0;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            switch (operand.Kind)
            {
                case SacredScriptArgumentKind.TypeId:
                    var resource = unchecked((uint)operand.Integer!.Value);
                    if (type == 0) type = resource; else equipment.Add(resource);
                    break;
                case SacredScriptArgumentKind.NullTerminatedString: name = operand.Text; break;
                case SacredScriptArgumentKind.TilePosition:
                case SacredScriptArgumentKind.TriggerFirstCorner:
                case SacredScriptArgumentKind.TriggerLastCorner:
                case SacredScriptArgumentKind.TriggerPatchPosition:
                    position = operand.Position ?? resolvePosition(operand.Text!);
                    placement = position.Value;
                    worldPosition = null;
                    break;
                case SacredScriptArgumentKind.WorldPosition:
                    worldPosition = operand.Position!.Value;
                    placement = new(unchecked(-worldPosition.Value.X), unchecked(-worldPosition.Value.Y), placement.Z);
                    position = null;
                    break;
                case SacredScriptArgumentKind.Facing: facing = checked((ushort)operand.Integer!.Value); break;
                case SacredScriptArgumentKind.CreatureGroup:
                    group = operand.Integer!.Value; explicitBehavior = true; break;
                case SacredScriptArgumentKind.CreatureLevel:
                    level = operand.Integer ?? resolveVariable?.Invoke(operand.Text!) ??
                        throw new InvalidDataException($"Unresolved CreateNPC level variable '{operand.Text}' at +0x{operand.Offset:X}.");
                    break;
                case SacredScriptArgumentKind.CreatureFlag2B7Mask08:
                    flag2B7Bit8 = operand.Integer!.Value;
                    break;
                case SacredScriptArgumentKind.CreatureNonAggressive: aggression = 0; break;
                case SacredScriptArgumentKind.CreatureAggressive: aggression = 1; break;
                case SacredScriptArgumentKind.CreatureBehavior1:
                    behavior = (behavior & 0xFFFF0001) | 1; explicitBehavior = true; break;
                case SacredScriptArgumentKind.CreatureBehavior0:
                    behavior &= 0xFFFF0000; explicitBehavior = true; break;
                case SacredScriptArgumentKind.CreatureBehavior2:
                    behavior = (behavior & 0xFFFF0002) | 2; explicitBehavior = true; break;
                case SacredScriptArgumentKind.CreatureBehavior4000:
                    behavior = (behavior & 0xFFFF4000) | 0x4000; explicitBehavior = true; break;
                case SacredScriptArgumentKind.CreatureBehavior10:
                    behavior = (behavior & 0xFFFF0010) | 0x10; aggression = 0; break;
                case SacredScriptArgumentKind.CreatureBehavior8:
                    behavior = (behavior & 0xFFFF0008) | 8; aggression = 0; break;
                case SacredScriptArgumentKind.CreatureBehavior20:
                    behavior = (behavior & 0xFFFF0020) | 0x20; aggression = 0; break;
                case SacredScriptArgumentKind.CreatureBehavior80:
                    behavior = (behavior & 0xFFFF0080) | 0x80; aggression = 0; break;
                case SacredScriptArgumentKind.CreatureBehavior400:
                    behavior = (behavior & 0xFFFF0400) | 0x400; aggression = 0; break;
                case SacredScriptArgumentKind.CreatureBehavior40:
                case SacredScriptArgumentKind.CreatureBehavior40Alias:
                    behavior = (behavior & 0xFFFF0040) | 0x40; break;
                case SacredScriptArgumentKind.CreatureTrader:
                    constructionFlags |= 4; explicitBehavior = true; break;
                case SacredScriptArgumentKind.CreatureComboMaster:
                    constructionFlags |= 8; explicitBehavior = true; break;
                case SacredScriptArgumentKind.CreatureNpcBehavior:
                    behavior = (behavior & 0xFFFF8000) | 0x8000; break;
                case SacredScriptArgumentKind.CreatureStationary:
                    behavior |= 0x08000000; explicitBehavior = true; break;
                default:
                    throw new InvalidDataException($"CreateNPC effect 0x{(byte)operand.Kind:X2} is unresolved at +0x{operand.Offset:X}.");
            }
        }
        return new(name, type, equipment.AsReadOnly(), position, facing, group, level, behavior, aggression, explicitBehavior)
            { WorldPosition = worldPosition, PlacementInput = placement, Flag2B7Mask08Input = flag2B7Bit8, ConstructionFlags = constructionFlags };
    }
}
