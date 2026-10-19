using Sacred.Assets.Executable;
using Sacred.Assets.GameBin;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World.Stairs;

namespace Sacred.GameplayHost;

/// <summary>Selected script data, including quest bytecode retained for future execution.
/// Loading this data does not execute quest callbacks.</summary>
public sealed class WorldCampaignScripts
{
    private WorldCampaignScripts(SacredCampaignFiles files)
    {
        Files = files;
        var gameDirectory = Directory.GetParent(files.DirectoryPath)?.Parent?.FullName;
        if (gameDirectory is not null)
        {
            try { Regions = SacredRegionDefinitions.Load(gameDirectory); }
            catch (InvalidDataException) { /* Already initialized regions do not require this lookup. */ }
            try { PopulationDefinitions = SacredPopulationDefinitions.Load(gameDirectory); }
            catch (InvalidDataException) { /* FillRegion retains an explicit dependency if absent. */ }
            var balance = Path.Combine(gameDirectory, "bin", "Balance.bin");
            if (File.Exists(balance)) PopulationBalance = PopulationBalanceBinReader.Load(balance);
        }
        StartupCode = File.ReadAllBytes(files.StartCodePath);
        FunctionCode = File.ReadAllBytes(files.FunctionCodePath);
        Vectors = File.ReadAllBytes(files.VectorsPath);
        Functions = SacredScriptFunctionReader.Read(Vectors.Span, FunctionCode.Length);
        var definitions = File.ReadAllBytes(files.DefPosPath);
        Positions = SacredDefPosPosition.ReadMany(definitions);
        Dialogs = SacredScriptDialogReader.Read(definitions);
        QuestCode = File.ReadAllBytes(files.QuestCodePath);
        QuestPoolCode = File.ReadAllBytes(files.QuestPoolCodePath);
        // Literal declarations are source metadata, including conditional blocks; they are not an executed startup snapshot.
        var literals = new Dictionary<string, SacredScriptPosition>();
        BaseTriggerDeclarations = Array.AsReadOnly(SacredCompiledScriptReader.Read(StartupCode)
            .Select(command => SacredBaseTriggerScriptReader.TryRead(command, literals, out var trigger) ? trigger : null)
            .OfType<SacredBaseTriggerDefinition>().ToArray());
        Console.WriteLine($"Campaign scripts loaded: {files.Name}, {BaseTriggerDeclarations.Count} literal base-trigger declarations; startup execution pending.");
    }

    public SacredCampaignFiles Files { get; }
    public SacredRegionDefinitions? Regions { get; }
    public SacredPopulationDefinitions? PopulationDefinitions { get; }
    public SacredPopulationBalance? PopulationBalance { get; }
    public ReadOnlyMemory<byte> StartupCode { get; }
    public ReadOnlyMemory<byte> FunctionCode { get; }
    public ReadOnlyMemory<byte> Vectors { get; }
    public IReadOnlyList<SacredScriptFunction> Functions { get; }
    public IReadOnlyList<SacredDefPosPosition> Positions { get; }
    public IReadOnlyList<SacredScriptDialog> Dialogs { get; }
    public ReadOnlyMemory<byte> QuestCode { get; }
    public ReadOnlyMemory<byte> QuestPoolCode { get; }
    public IReadOnlyList<SacredBaseTriggerDefinition> BaseTriggerDeclarations { get; }

    /// <summary>Native startup instruction order. The caller must execute each yielded handler and
    /// supply predicates from that changing state; enumeration alone does not initialize the world.</summary>
    public IEnumerable<SacredScriptCommand> ReadStartupCommands(Func<SacredScriptCommand, bool> predicate) =>
        SacredScriptControlFlowReader.Read(StartupCode, predicate);

    public static WorldCampaignScripts Load(SacredCampaignFiles files) => new(files);
}
