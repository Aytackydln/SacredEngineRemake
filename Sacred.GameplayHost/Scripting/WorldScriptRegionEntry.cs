using System.Globalization;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Reached 492C90 lifecycle after actual sector/hero-region selection.
/// Shares Init's native +350 map. Callback service suspension cannot mark the
/// target alive or skip pending functions on retry. Native EAX is ignored.</summary>
public sealed class WorldScriptRegionEntry(WorldScriptRegionInitialization lifecycle)
{
    private ushort[]? _pending;
    private int _cursor;
    private uint _region;
    /// <summary>Original first-world current-region sentinel at 94E088.</summary>
    public uint EnteredRegion { get; set; }=uint.MaxValue;
    public bool IsEntering { get; private set; }
    public bool IsPending=>_pending is not null;
    public IReadOnlyList<ushort> PendingFunctions=>_pending?.Skip(_cursor).ToArray() ?? [];

    /// <returns>False for a native no-op, true for delivered entry, null for a
    /// pending callback service. None of these establish sector readiness.</returns>
    public bool? TryEnter(uint region, bool sectorPresent, Func<string,uint?> cached,
        Func<string,int> find, Func<ushort,bool> invoke)
    {
        if (_pending is not null && _region!=region)
            throw new InvalidOperationException("Finish the pending region entry before selecting another region.");
        if (_pending is null)
        {
            if (!sectorPresent || !lifecycle.Records.TryGetValue(region,out var row) || (row.Tag & 0xff00)!=0) return false;
            _region=region;
            lifecycle.CurrentRegion=region;
            var name=string.Create(CultureInfo.InvariantCulture,$"Region{region}Enter");
            uint? packed=cached(name);
            if(packed is null)
            {
                var index=find(name);
                if(index!=0)packed=unchecked((uint)index);
            }
            _pending=packed is { } selected ? (selected & 0xffff0000)!=0 ? [(ushort)(selected>>16),(ushort)selected] : [(ushort)selected] : [];
            _cursor=0;
        }
        IsEntering=true;
        try
        {
            while(_cursor<_pending.Length)
            {
                if(!invoke(_pending[_cursor]))return null;
                _cursor++;
            }
            // The scan happens BEFORE marking the new record alive.
            if(unchecked((int)EnteredRegion)<0)
                foreach(var pair in lifecycle.Records.OrderBy(pair=>pair.Key))
                    if((pair.Value.Tag & 0xff00)!=0){EnteredRegion=pair.Key;break;}
            lifecycle.MarkEntered(_region);
            lifecycle.CurrentRegion=uint.MaxValue;
            _pending=null;
            return true;
        }
        finally{IsEntering=false;}
    }
}
