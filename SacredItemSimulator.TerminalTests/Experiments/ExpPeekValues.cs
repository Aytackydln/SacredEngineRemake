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

        Console.WriteLine("Read total weapons: " + sacredGameData.GamePakStore.Weapons.Count);
        Console.WriteLine("Read total items: " + sacredGameData.GamePakStore.Items.Count);
    }
}