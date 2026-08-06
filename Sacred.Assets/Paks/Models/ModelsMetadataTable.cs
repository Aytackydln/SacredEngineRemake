using System.Buffers.Binary;
using System.Text;

namespace Sacred.Assets.Paks.Models;

internal sealed class ModelsMetadataTable
{
    private const int HeaderSize = 0x118;
    private const int ModelRecordSize = 1194;
    private const int MotionRecordSize = 256;
    private const int NameSize = 32;

    private static readonly int[] IdleMotionOffsets =
        [116, 120, 124, 128, 132, 136, 140, 144, 148, 152, 156, 160, 164, 1052];
    private static readonly int[] WalkMotionOffsets =
        [220, 224, 228, 232, 236, 240, 244, 248, 252, 256, 260, 264, 268, 1060];
    private static readonly int[] FightingIdleMotionOffsets =
        [168, 172, 176, 180, 184, 188, 192, 196, 200, 204, 208, 212, 216, 1056];
    private static readonly int[] RunMotionOffsets =
        [272, 276, 280, 284, 288, 292, 296, 300, 304, 308, 312, 316, 320, 1064];
    private static readonly int[] DefendMotionOffsets =
        [324, 328, 332, 336, 340, 344, 348, 352, 356, 360, 364, 368, 372, -1];
    private static readonly int[] AttackMotionOffsets =
        [428, 448, 468, 488, 508, 528, 548, 568, 588, 608, 628, 648, 668, 1076];

    private readonly string[] _motionNames;
    private readonly Dictionary<string, ModelMotionTable> _modelMotions;

    private ModelsMetadataTable(
        string[] motionNames,
        Dictionary<string, ModelMotionTable> modelMotions)
    {
        _motionNames = motionNames;
        _modelMotions = modelMotions;
    }

    public static ModelsMetadataTable Empty { get; } =
        new([], new Dictionary<string, ModelMotionTable>(StringComparer.OrdinalIgnoreCase));

    public static ModelsMetadataTable Load(string path)
    {
        if (!File.Exists(path))
            return Empty;

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < HeaderSize)
            throw new InvalidDataException("Models.tmp is shorter than its header.");

        var modelCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x10, 4));
        var motionCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x14, 4));
        var motionTableOffset = HeaderSize + (long)modelCount * ModelRecordSize;
        var requiredLength = motionTableOffset + (long)motionCount * MotionRecordSize;
        if (modelCount > int.MaxValue || motionCount > int.MaxValue ||
            motionTableOffset < HeaderSize || requiredLength > bytes.Length)
            throw new InvalidDataException("Models.tmp has invalid model or motion table bounds.");

        var motionNames = new string[checked((int)motionCount)];
        for (var index = 0; index < motionNames.Length; index++)
        {
            var offset = checked((int)motionTableOffset + index * MotionRecordSize);
            motionNames[index] = ReadName(bytes.AsSpan(offset, NameSize));
        }

        var modelMotions = new Dictionary<string, ModelMotionTable>(
            checked((int)modelCount),
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < modelCount; index++)
        {
            var offset = checked(HeaderSize + index * ModelRecordSize);
            var record = bytes.AsSpan(offset, ModelRecordSize);
            var modelName = ReadName(record[..NameSize]);
            if (!string.IsNullOrWhiteSpace(modelName))
                modelMotions.TryAdd(modelName, ReadMotionTable(record));
        }

        return new ModelsMetadataTable(motionNames, modelMotions);
    }

    public bool TryGetMotionName(uint motionIndex, out string name)
    {
        if (motionIndex < _motionNames.Length &&
            !string.IsNullOrWhiteSpace(_motionNames[motionIndex]))
        {
            name = _motionNames[motionIndex];
            return true;
        }

        name = string.Empty;
        return false;
    }

    public bool TryGetMotionName(
        string modelName,
        CharacterMotionKind kind,
        CharacterMotionWeaponStyle weaponStyle,
        out string name)
    {
        if (!_modelMotions.TryGetValue(modelName, out var table))
        {
            name = string.Empty;
            return false;
        }

        var motionIndex = table.GetWithFallback(kind, weaponStyle);
        if (motionIndex == 0)
        {
            name = string.Empty;
            return false;
        }

        return TryGetMotionName(motionIndex, out name);
    }

    private static ModelMotionTable ReadMotionTable(ReadOnlySpan<byte> record) => new(
        ReadMotionIndexes(record, IdleMotionOffsets),
        ReadMotionIndexes(record, WalkMotionOffsets),
        ReadMotionIndexes(record, RunMotionOffsets),
        ReadMotionIndexes(record, FightingIdleMotionOffsets),
        ReadMotionIndexes(record, DefendMotionOffsets),
        ReadMotionIndexes(record, AttackMotionOffsets));

    private static uint[] ReadMotionIndexes(ReadOnlySpan<byte> record, IReadOnlyList<int> offsets)
    {
        var result = new uint[offsets.Count];
        for (var index = 0; index < offsets.Count; index++)
        {
            var offset = offsets[index];
            if (offset >= 0 && offset + sizeof(uint) <= record.Length)
                result[index] = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(offset, sizeof(uint)));
        }

        return result;
    }

    private static string ReadName(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end >= 0 ? bytes[..end] : bytes);
    }

    private sealed record ModelMotionTable(
        uint[] Idle,
        uint[] Walk,
        uint[] Run,
        uint[] FightingIdle,
        uint[] Defend,
        uint[] Attack)
    {
        public uint GetWithFallback(CharacterMotionKind kind, CharacterMotionWeaponStyle style)
        {
            var motions = MotionsFor(kind);
            var motion = Get(motions, style);
            if (motion == 0 && style != CharacterMotionWeaponStyle.OneHanded)
                motion = Get(motions, CharacterMotionWeaponStyle.OneHanded);
            if (motion == 0 && style != CharacterMotionWeaponStyle.BareHanded)
                motion = Get(motions, CharacterMotionWeaponStyle.BareHanded);
            if (motion == 0)
                motion = motions.FirstOrDefault(static candidate => candidate != 0);

            // Some playable records do not contain a dedicated guard clip. Their authored
            // fighting-idle pose is the closest guard stance and keeps the state visible.
            if (motion == 0 && kind == CharacterMotionKind.Defend)
            {
                motion = Get(FightingIdle, style);
                if (motion == 0 && style != CharacterMotionWeaponStyle.OneHanded)
                    motion = Get(FightingIdle, CharacterMotionWeaponStyle.OneHanded);
                if (motion == 0)
                    motion = Get(FightingIdle, CharacterMotionWeaponStyle.BareHanded);
                if (motion == 0)
                    motion = FightingIdle.FirstOrDefault(static candidate => candidate != 0);
            }

            return motion;
        }

        private uint[] MotionsFor(CharacterMotionKind kind) => kind switch
        {
            CharacterMotionKind.Idle => Idle,
            CharacterMotionKind.Walk => Walk,
            CharacterMotionKind.Run => Run,
            CharacterMotionKind.Defend => Defend,
            CharacterMotionKind.Attack => Attack,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        private static uint Get(IReadOnlyList<uint> motions, CharacterMotionWeaponStyle style)
        {
            var index = (int)style;
            return (uint)index < (uint)motions.Count ? motions[index] : 0;
        }
    }
}
