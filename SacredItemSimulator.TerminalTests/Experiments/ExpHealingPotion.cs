namespace SacredItemSimulator.TerminalTests.Experiments;

public class ExpHealingPotion : IExperiment
{
    public void Run(SacredGameData sacredGameData)
    {
        const string it = "Lesser Healing Potion";
        var t = sacredGameData.GameResStore.Strings.
            Where(kv => kv.Value == it)
            .ToList();
        var hp = sacredGameData.GameResStore.Strings[77092496];
    }
}