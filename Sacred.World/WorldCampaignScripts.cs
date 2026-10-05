using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World.Stairs;

namespace Sacred.World;

/// <summary>Selected script data, including quest bytecode retained for future execution.
/// Loading this data does not execute quest callbacks.</summary>
public sealed class WorldCampaignScripts
{
    private WorldCampaignScripts(SacredCampaignFiles files)
    {
        Files = files;
        StartupCode = File.ReadAllBytes(files.StartCodePath);
        FunctionCode = File.ReadAllBytes(files.FunctionCodePath);
        Vectors = File.ReadAllBytes(files.VectorsPath);
        Functions = SacredScriptFunctionReader.Read(Vectors.Span, FunctionCode.Length);
        Positions = SacredDefPosPosition.ReadMany(File.ReadAllBytes(files.DefPosPath));
        QuestCode = File.ReadAllBytes(files.QuestCodePath);
        QuestPoolCode = File.ReadAllBytes(files.QuestPoolCodePath);
    }

    public SacredCampaignFiles Files { get; }
    public ReadOnlyMemory<byte> StartupCode { get; }
    public ReadOnlyMemory<byte> FunctionCode { get; }
    public ReadOnlyMemory<byte> Vectors { get; }
    public IReadOnlyList<SacredScriptFunction> Functions { get; }
    public IReadOnlyList<SacredDefPosPosition> Positions { get; }
    public ReadOnlyMemory<byte> QuestCode { get; }
    public ReadOnlyMemory<byte> QuestPoolCode { get; }

    public static WorldCampaignScripts Load(SacredCampaignFiles files) => new(files);
}
