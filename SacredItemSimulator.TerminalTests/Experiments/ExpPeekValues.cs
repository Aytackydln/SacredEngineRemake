namespace SacredItemSimulator.TerminalTests.Experiments;

public class ExpPeekValues : IExperiment
{
    public void Run(SacredGameData sacredGameData)
    {
        var weaponPeek = sacredGameData.GamePakStore
            .Weapons
            .Values
            .Take(100)
            .ToList();

        var itemsPeek = sacredGameData.GamePakStore
            .Items
            .Values
            .Take(100)
            .ToList();
        
        var itemDescByteOffset10Values = sacredGameData.GamePakStore
            .Weapons
            .Values
            .Select(weapon => weapon.SpanX[0]) // byte at offset 10 in the weapon entry
            .Distinct()
            .ToList();
        
        var itemDescByteOffset11Values = sacredGameData.GamePakStore
            .Weapons
            .Values
            .Select(weapon => weapon.SpanX[1]) // byte at offset 12 in the weapon entry
            .Distinct()
            .ToList();

        Console.WriteLine("Read total weapons: " + sacredGameData.GamePakStore.Weapons.Count);
        Console.WriteLine("Read total items: " + sacredGameData.GamePakStore.Items.Count);
    }
}