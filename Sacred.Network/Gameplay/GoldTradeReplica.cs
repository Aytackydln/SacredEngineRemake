namespace Sacred.Network.Gameplay;

/// <summary>Native trade-panel stores; panel existence is an explicit UI input.</summary>
public sealed class GoldTradeReplica
{
    private readonly List<GoldTradeEvent> _requests = [];
    public GoldTradeReplica() => Requests = _requests.AsReadOnly();
    public IReadOnlyList<GoldTradeEvent> Requests { get; }
    public bool PanelAvailable { get; set; }
    public uint ActivePartner { get; private set; }
    public uint? PanelPartner { get; private set; }
    public uint Amount { get; private set; }
    public uint? SecondAmount { get; private set; }
    public bool FlagBB4 { get; private set; }
    public bool FlagBB5 { get; private set; }
    public bool FlagBB6 { get; private set; }
    public bool FlagBB7 { get; private set; }
    public bool FlagBB8 { get; private set; }
    public ReadOnlyMemory<byte> InventoryData { get; private set; }
    internal bool Apply(GoldTradeEvent entry)
    {
        if (!entry.MatchesSender) return false;
        var op = entry.Operation;
        if (op is 0x21 or 0x2E or 0x30 || op is < 0x20 or > 0x31) return false;
        if (op == 0x20) ActivePartner = entry.Message.Sender;
        if (op is 0x22 or 0x23 or 0x25) ActivePartner = 0;
        if (PanelAvailable)
        {
            switch (op)
            {
                case 0x25: PanelPartner = entry.Message.Sender; break;
                case 0x26: InventoryData = entry.InventoryData; FlagBB5 = false; break;
                case 0x27: Amount = entry.Amount!.Value; FlagBB4 = FlagBB5 = false; break;
                case 0x28: case 0x2A: FlagBB7 = true; FlagBB5 = op == 0x28; break;
                case 0x29: case 0x2B: FlagBB8 = false; FlagBB6 = true; FlagBB4 = op == 0x29; break;
                case 0x2C: FlagBB7 = false; break;
                case 0x2D: FlagBB6 = false; break;
                case 0x2F:
                    InventoryData = entry.InventoryData; Amount = entry.Amount!.Value; SecondAmount = entry.SecondAmount;
                    FlagBB4 = FlagBB5 = false; break;
            }
        }
        _requests.Add(entry); return true;
    }
    internal void Clear()
    {
        _requests.Clear(); PanelAvailable = false; ActivePartner = Amount = 0; PanelPartner = SecondAmount = null;
        FlagBB4 = FlagBB5 = FlagBB6 = FlagBB7 = FlagBB8 = false; InventoryData = default;
    }
}
