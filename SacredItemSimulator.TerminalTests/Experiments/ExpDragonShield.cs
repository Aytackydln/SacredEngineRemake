using Sacred.Assets;

namespace SacredItemSimulator.TerminalTests.Experiments;

public class ExpDragonShield : IExperiment
{
    public void Run(SacredGameData sacredGameData)
    {
        var dragonShieldWeapons = sacredGameData.GamePakStore
            .Weapons
            .Values
            .Where(w => w.Name == "Drachenschild")
            .ToList();

        var dragonShieldItems = sacredGameData.GamePakStore
            .Items
            .Values
            .Where(i => i.ModelDesc.ModelName == "SHIELD_KITE.GRN")
            .ToList();

        var kiteTexture = sacredGameData.GamePakStore.Textures["SHIELD_KITE02.TGA"];

        Console.WriteLine("Dragon Shield Weapons:");
    }
}