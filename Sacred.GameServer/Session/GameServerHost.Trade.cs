using System.Buffers.Binary;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Session;

/// <summary>One player's side of a trade (record offsets of the original: state +0x2D0, partner +0x2D4, time +0x2D8, gold +0x2DC,
/// offered object references +0x2E0 (0x23 words), their item data +0x36C (0x6B words)).</summary>
public sealed class TradeState
{
    public const int OfferWords = 0x23, DataBytes = 0x1AC;
    /// <summary>0 none, 1 invitation sent, 2 locked in, 3 trading, 4 offer rejected by the validation.</summary>
    public int State;
    public byte Partner;
    public long TimeMs;
    public uint Gold;
    public readonly uint[] Offer = new uint[OfferWords];
    public readonly byte[] Data = new byte[DataBytes];

    public bool OfferEmpty => Offer.All(o => o == 0);
    public void ResetState() { TimeMs = 0; State = 0; Partner = 0; }
    public void Reset() { ResetState(); Gold = 0; Array.Clear(Offer); Array.Clear(Data); }
}

/// <summary>
/// 0x198 (case 0x4C850E, <c>[partner slot][sub command][...]</c>): the original's player-to-player trade, a state machine per player
/// that pairs two heroes, relays the offers and answers both sides with 0x198 messages <c>[other slot][code]</c> (origin = the other
/// player). Sub commands from a client: 0x20 invite, 0x21 accept, 0x22 withdraw the invitation, 0x23 decline, 0x26 offer items,
/// 0x27 gold, 0x28 lock in, 0x2A unlock, 0x2E re-check, 0x30 cancel; answers 0x24 busy, 0x25 started, 0x29/0x2B lock state,
/// 0x2A/0x2C offer accepted/rejected, 0x2F the final exchange (partner gold, own gold, partner item data), 0x31 trade over.
/// TODO (0x4C8978..): the original validates every offer against the objects of its own world (FUN_0045C010) and executes the
/// exchange there (FUN_0040F840); this server relays, so every offer counts as valid and the exchange is only announced.
/// </summary>
public sealed partial class GameServerHost
{
    private PlayerSession? TradePartnerOf(uint slot) =>
        slot is >= 1 and <= MaxSlots && _slots[slot] is { } p && p.State >= SessionState.SlotAssigned && p.InGame ? p : null;

    private void TradeSend(PlayerSession target, uint other, uint code)
    {
        Span<byte> body = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(body, other);
        BinaryPrimitives.WriteUInt32LittleEndian(body[4..], code);
        SendMessage(target, 0x198, 0, (byte)other, body);
    }

    /// <summary>The incoming message with its first word replaced, sent on to <paramref name="target"/> (origin = <paramref name="origin"/>).</summary>
    private void TradeRelay(PlayerSession target, in GoldMessageView m, uint first, byte origin)
    {
        var copy = m.Payload.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(copy, first);
        SendMessage(target, 0x198, 0, origin, copy);
    }

    private void OnTrade(PlayerSession s, in GoldMessageView m)
    {
        if (m.Payload.Length < 8) return;
        var partnerSlot = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload);
        var sub = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload[4..]);
        var me = s.Trade;
        var p = TradePartnerOf(partnerSlot);
        if (p is null)
        {
            // partner unknown, gone or not in the world: own state cleared, "trade over" to both sides
            me.ResetState();
            TradeSend(s, partnerSlot, 0x31);
            if (partnerSlot is >= 1 and <= MaxSlots && _slots[partnerSlot] is { } other && other.State >= SessionState.SlotAssigned)
                TradeRelay(other, m, 0, 0);
            return;
        }
        var you = p.Trade;
        var mine = (uint)s.Slot;
        switch (sub)
        {
            case 0x20 when me.State == 0:
                if (you.State == 0)
                {
                    me.TimeMs = _clock.ElapsedMilliseconds; me.State = 1; me.Partner = p.Slot;
                    TradeRelay(p, m, mine, s.Slot);
                }
                else TradeSend(s, partnerSlot, 0x24);
                break;
            case 0x21 when you.State == 1:
                me.Reset(); you.Reset();
                me.State = 3; you.State = 3;
                me.Partner = p.Slot; you.Partner = s.Slot;
                TradeSend(s, partnerSlot, 0x25);
                TradeSend(p, mine, 0x25);
                break;
            case 0x22 when me.State == 1:
                me.ResetState();
                TradeSend(s, partnerSlot, 0x31);
                TradeRelay(p, m, 0, 0);
                break;
            case 0x23 when you.State == 1:
                you.ResetState();
                TradeRelay(p, m, 0, 0);
                TradeRelay(s, m, 0, 0);
                break;
            case 0x26 when me.State == 3 && you.State is 2 or 3 or 4:
                StoreOffer(me, m.Payload);
                you.State = 3;                                       // offer accepted (the validation against the world is a TODO)
                TradeSend(s, partnerSlot, 0x2A);
                TradeSend(p, mine, 0x2B);
                TradeRelay(p, m, mine, s.Slot);
                break;
            case 0x27 when me.State == 3 && you.State is 2 or 3 or 4:
                if (m.Payload.Length >= 12) me.Gold = BinaryPrimitives.ReadUInt32LittleEndian(m.Payload[8..]);
                if (you.State == 2) { you.State = 3; TradeSend(s, partnerSlot, 0x2A); }
                TradeRelay(p, m, mine, s.Slot);
                break;
            case 0x28 when me.State == 3:
                me.State = 2;
                TradeSend(s, partnerSlot, 0x29);
                TradeRelay(p, m, mine, s.Slot);
                if (you.State == 2) CompleteTrade(s, p);
                break;
            case 0x2A when me.State == 2:
                me.State = 3;
                TradeSend(s, partnerSlot, 0x2B);
                TradeRelay(p, m, mine, s.Slot);
                break;
            case 0x2E when me.State is 2 or 3 or 4:
                if (me.State == 4) me.State = 3;                      // the re-check of the partner's offer passes
                TradeSend(s, partnerSlot, 0x2B);
                TradeSend(p, mine, 0x2A);
                break;
            case 0x30 when me.State is 2 or 3 or 4:
                me.Reset(); you.Reset();
                TradeSend(s, partnerSlot, 0x31);
                TradeSend(p, mine, 0x31);
                break;
            default:
                break;                                               // wrong state or unknown sub command: logged only in the original
        }
    }

    /// <summary>0x26 [partner][0x26][count][count object references][item data]: the offered references and the data behind them.</summary>
    private static void StoreOffer(TradeState t, ReadOnlySpan<byte> payload)
    {
        Array.Clear(t.Offer); Array.Clear(t.Data);
        var count = payload.Length >= 12 ? BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]) : 0;
        var words = (int)Math.Min(count, (uint)TradeState.OfferWords);
        for (var i = 0; i < words && 12 + (i + 1) * 4 <= payload.Length; i++) t.Offer[i] = BinaryPrimitives.ReadUInt32LittleEndian(payload[(12 + i * 4)..]);
        var dataStart = 12 + (long)count * 4;
        if (dataStart < payload.Length)
        {
            var length = (int)Math.Min(payload.Length - dataStart, TradeState.DataBytes);
            payload.Slice((int)dataStart, length).CopyTo(t.Data);
        }
    }

    /// <summary>Both sides locked in (0x28): an empty trade just ends (0x31); otherwise each side gets the other's gold and item data
    /// in a 0x2F and both trades end.</summary>
    private void CompleteTrade(PlayerSession s, PlayerSession p)
    {
        var me = s.Trade; var you = p.Trade;
        if (me.Gold == 0 && you.Gold == 0 && me.OfferEmpty && you.OfferEmpty)
        {
            TradeSend(s, p.Slot, 0x31);
            TradeSend(p, s.Slot, 0x31);
        }
        else
        {
            var body = new byte[16 + TradeState.DataBytes];
            void Exchange(PlayerSession to, PlayerSession other, TradeState theirs, TradeState own)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(body, other.Slot);
                BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), 0x2F);
                BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), theirs.Gold);
                BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(12), own.Gold);
                theirs.Data.CopyTo(body.AsSpan(16));
                SendMessage(to, 0x198, 0, other.Slot, body);
            }
            Exchange(s, p, you, me);
            Exchange(p, s, me, you);
            if (_config.Auth.Enabled) { MarkHeroChanged(s); MarkHeroChanged(p); }
        }
        me.Reset(); you.Reset();
    }

    /// <summary>A player leaves (case 0x4CA4F9 / FUN_004D6AC0 path): its partner's trade ends with a 0x31, then its own state is cleared.</summary>
    private void CancelTradeOf(PlayerSession s)
    {
        var me = s.Trade;
        if (me.State != 0 && TradePartnerOf(me.Partner) is { } partner && partner.Trade.Partner == s.Slot)
        {
            partner.Trade.ResetState();
            TradeSend(partner, s.Slot, 0x31);
        }
        me.ResetState();
    }
}
