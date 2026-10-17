using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Sacred.Core.UI;

namespace Sacred.Assets.Executable;

/// <summary>Reads the five original taskbar unlock records, including wrapped Gold executables.</summary>
public sealed class SacredQuickSlotDefinitions
{
    public const int MaximumSlots = 5;
    public IReadOnlyList<SacredQuickSlotLevelLayout> Levels { get; }

    private SacredQuickSlotDefinitions(SacredQuickSlotLevelLayout[] levels) => Levels = Array.AsReadOnly(levels);

    public int GetSlotCount(ushort level)
    {
        for (var index = Levels.Count - 1; index >= 0; index--)
            if (level >= Levels[index].MinimumLevel) return Levels[index].SlotCount;
        return MaximumSlots; // Native lookup fallback.
    }

    public static SacredQuickSlotDefinitions Read(string executablePath)
    {
        using var stream = File.OpenRead(executablePath);
        using var executable = new PEReader(stream);
        SacredQuickSlotLevelLayout[]? found = null;
        foreach (var section in executable.PEHeaders.SectionHeaders)
        {
            const SectionCharacteristics required = SectionCharacteristics.ContainsInitializedData | SectionCharacteristics.MemWrite;
            if ((section.SectionCharacteristics & required) != required) continue;
            var data = executable.GetSectionData(section.VirtualAddress).GetContent();
            var bytes = data.AsSpan();
            for (var offset = 0; offset <= bytes.Length - MaximumSlots * 4; offset += 2)
            {
                var records = MemoryMarshal.Cast<byte, SacredQuickSlotLevelLayout>(bytes.Slice(offset, MaximumSlots * 4));
                if (records[0].MinimumLevel != 0) continue;
                var valid = true;
                for (var index = 0; index < MaximumSlots; index++)
                    valid &= records[index].SlotCount == index + 1 &&
                        (index == 0 || records[index].MinimumLevel > records[index - 1].MinimumLevel);
                if (!valid) continue;
                if (found is not null) throw new InvalidDataException("Ambiguous native taskbar unlock tables.");
                found = records.ToArray();
            }
        }
        return new(found ?? throw new InvalidDataException("No native taskbar unlock table was found."));
    }
}
