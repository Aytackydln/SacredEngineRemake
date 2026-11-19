using System.Text;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Ordered native SetVar state; replacement changes the first matching value and keeps name storage.</summary>
public sealed class WorldScriptVariables
{
    private readonly List<GoldScriptVariableRecord> _records = [];
    private readonly List<GoldMessage> _publications = [];
    public IReadOnlyList<GoldMessage> Publications=>_publications.AsReadOnly();
    internal void ClearEnteredBit(string name,int bit,byte owner)
    {
        var index=FindIndex(name);
        if(index<0)throw new NotSupportedException("Entered variable bit clear needs its existing ordinary record.");
        var record=_records[index].WithValue(_records[index].Value & ~(1<<bit));
        _records[index]=record;
        _publications.Add(GoldScriptVariableCodec.Create(owner,owner,record,(uint)index));
    }
    public WorldScriptVariables() => Records = _records.AsReadOnly();
    public IReadOnlyList<GoldScriptVariableRecord> Records { get; }
    public int? Find(string name)
    {
        var index = FindIndex(name);
        return index < 0 ? null : _records[index].Value;
    }
    public void Import(IEnumerable<GoldScriptVariableRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var copy = records.ToArray();
        _records.Clear();
        _records.AddRange(copy);
    }
    public void Set(string name, int value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Any(character => character > byte.MaxValue)) throw new ArgumentException("Variable names must preserve original Latin-1 bytes.", nameof(name));
        var index = FindIndex(name);
        if (name.Length == 0) return;
        if (index < 0) _records.Add(GoldScriptVariableRecord.Create(Encoding.Latin1.GetBytes(name), value));
        else _records[index] = _records[index].WithValue(value);
    }
    /// <summary>Gold 0x49C930 only changes an existing variable when the wrapped sum is not less
    /// than its previous signed value. It does not implement general signed arithmetic.</summary>
    public void Add(string name, int amount)
    {
        var index = FindIndex(name);
        if (index < 0) { Set(name, amount); return; }
        var current = _records[index].Value;
        var sum = unchecked(current + amount);
        if (sum >= current) _records[index] = _records[index].WithValue(sum);
    }
    private int FindIndex(string name)
    {
        for (var i = 0; i < _records.Count; i++)
            if (WorldScriptNameComparer.Instance.Equals(_records[i].Name, name)) return i;
        return -1;
    }
}
