using System.Collections.ObjectModel;
using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Known writes into the native +0x7684 block; its first 128 bytes form
/// bank 1's first fixed block. Untouched words stay unknown;
/// a short command overwrites its prefix instead of resetting the entire block.</summary>
public sealed class WorldScriptStartupParameters
{
    private readonly Dictionary<int, int> _words = [];
    public WorldScriptStartupParameters() => Words = new ReadOnlyDictionary<int, int>(_words);
    public WorldScriptStartupParameters(ReadOnlySpan<byte> initializedBlock) : this()
    {
        if(initializedBlock.Length!=SacredScriptStartupParameterLayout.BankBlockSize)
            throw new ArgumentException("Expected complete original parameter initialization.",nameof(initializedBlock));
        for(var i=0;i<SacredScriptStartupParameterLayout.BankWordCount;i++)
            _words.Add(i,BinaryPrimitives.ReadInt32LittleEndian(initializedBlock[(i*4)..]));
    }
    public byte[] ToBankBlock()
    {
        var bytes=new byte[SacredScriptStartupParameterLayout.BankBlockSize];
        for(var i=0;i<SacredScriptStartupParameterLayout.BankWordCount;i++)
        {
            if(!_words.TryGetValue(i,out var value))throw new InvalidDataException($"Parameter word {i} requires original reset provenance.");
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i*4),value);
        }
        return bytes;
    }
    public IReadOnlyDictionary<int, int> Words { get; }
    internal void Set(IReadOnlyList<int> values)
    {
        for (var i = 0; i < values.Count; i++) _words[i] = values[i];
    }
}
