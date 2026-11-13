using Sacred.Core.GameBin.Scripts;
using System.Buffers.Binary;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold unresolved SetObjState: append the first native C-locale-equivalent name only.
/// CreateObj lookup, overrides and swap removal are compared; downstream object
/// effects remain separate. This private queue is not the bank-2 callback table.</summary>
public sealed class WorldScriptDeferredObjectStates
{
    private readonly List<WorldScriptDeferredObjectState> _records=[];
    public WorldScriptDeferredObjectStates()=>Records=_records.AsReadOnly();
    public IReadOnlyList<WorldScriptDeferredObjectState> Records { get; }
    public bool AppendMissing(SacredScriptObjectStateChange change)
    {
        if (string.IsNullOrEmpty(change.Name) || change.Name.Length>=SacredScriptDeferredObjectStateLayout.NameLength ||
            change.Name.Any(value=>value>255 || value==0) ||
            change.Open<0 && change.Locked<0) return false;
        if (_records.Any(row=>WorldScriptNameComparer.Instance.Equals(row.Name,change.Name))) return false;
        _records.Add(new(change.Name,change.Open,change.Locked));return true;
    }
    public WorldScriptDeferredObjectState? FindForCreation(string? name)=>_records.FirstOrDefault(
        row=>WorldScriptNameComparer.Instance.Equals(row.Name,name));

    /// <summary>Gold 0x486BAB..0x486CB2. Call only when the caller can apply the
    /// returned state: native consumes the request even when neither half changes.</summary>
    public bool TryConsumeForCreation(string name,WorldScriptObjectCreationState initial,
        out WorldScriptObjectCreationState result)
    {
        var index=_records.FindIndex(row=>WorldScriptNameComparer.Instance.Equals(row.Name,name));
        result=initial;
        if(index<0)return false;
        result=initial.Apply(_records[index]);
        _records[index]=_records[^1];
        _records.RemoveAt(_records.Count-1);
        return true;
    }

    /// <summary>Count plus 68-byte rows from interpreter save 0x46715B. Only
    /// post-NUL name tails are normalized. This is not a world-bank section.</summary>
    public byte[] ToNormalizedSaveBlock()
    {
        var bytes=new byte[checked(sizeof(uint)+_records.Count*SacredScriptDeferredObjectStateLayout.SerializedSize)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes,(uint)_records.Count);
        for(var i=0;i<_records.Count;i++)
            _records[i].ToNormalizedPayload().CopyTo(bytes,sizeof(uint)+i*SacredScriptDeferredObjectStateLayout.SerializedSize);
        return bytes;
    }
}
