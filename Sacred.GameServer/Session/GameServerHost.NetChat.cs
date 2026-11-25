using System.Buffers.Binary;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Session;

/// <summary>
/// The chat carrier of the net channels (<see cref="NetChatWire"/>): the handshake runs on it, and channels use it for every player that
/// has not agreed to the custom message types (or while it has not yet). The server never writes a frame to a player that did not announce itself (kind <c>h</c>, sent by Gear clients on entering the game): plain clients would show the frames as chat. Client to server: private chat to slot 0 (the original server
/// ignores it). Server to client: private chat from slot 0, paced per session. The caps are the custom path's: token bucket, messages in
/// flight, channels, message size (64 KB on chat), reassembly memory, fragment timeout.
/// </summary>
public sealed partial class GameServerHost
{
    private const int ChatMaxQueued = 1024 * 1024, ChatQueryEveryMs = 5000, ChatQueriesMax = 6;
    private const int ChatMaxBody = 65536;

    internal sealed class ChatInbound
    {
        public int Total, NextIndex;
        public List<byte> Bytes = new();
        public long LastMs;
    }

    // ---- receiving: private chat to slot 0 -----------------------------------------------------------------------------

    /// <summary>A client's private chat to slot 0 (<paramref name="text"/>: UTF-16LE). Always consumed: the server has no player 0 to relay to.</summary>
    private void NetChatFrame(PlayerSession s, ReadOnlySpan<byte> text)
    {
        var options = _config.NetOps;
        if (!options.Enabled || !NetChatWire.TryRead(text, out var frame)) return;
        var state = NetStateOf(s);
        var now = _clock.ElapsedMilliseconds;
        if (!TakeNetTokens(state, text.Length, now)) { NetStats.RateLimited++; state.ChatIn.Remove(frame.Number); return; }
        state.ChatIn.TryGetValue(frame.Number, out var message);
        if (frame.Index == 0)
        {
            if (message is not null) { state.ReassemblyBytes -= message.Bytes.Count; state.ChatIn.Remove(frame.Number); }
            if (state.ChatIn.Count >= options.MaxInFlight) { NetStats.TooManyInFlight++; return; }
            if (frame.Length < 2 || frame.Length > NetChatWire.MaxMessage) { NetStats.TooLarge++; return; }
            message = new ChatInbound { Total = frame.Length };
            state.ChatIn[frame.Number] = message;
        }
        else if (message is null || frame.Index != message.NextIndex) { NetStats.Orphans++; if (message is not null) { state.ReassemblyBytes -= message.Bytes.Count; state.ChatIn.Remove(frame.Number); } return; }
        message.LastMs = now;
        var take = NetChatWire.BytesIn(message.Total, frame.Index);
        if (state.ReassemblyBytes + take > options.ReassemblyPerSession || NetReassemblyTotal() + take > options.ReassemblyTotal)
        {
            NetStats.ReassemblyLimit++; state.ReassemblyBytes -= message.Bytes.Count; state.ChatIn.Remove(frame.Number); return;
        }
        var before = message.Bytes.Count;
        if (!NetChatWire.Unpack(frame.Packed, take, message.Bytes)) { NetStats.Malformed++; state.ReassemblyBytes -= before; state.ChatIn.Remove(frame.Number); return; }
        state.ReassemblyBytes += message.Bytes.Count - before;
        message.NextIndex = frame.Index + 1;
        if (message.Bytes.Count < message.Total) return;
        state.ChatIn.Remove(frame.Number);
        state.ReassemblyBytes -= message.Bytes.Count;
        OnNetChatMessage(s, state, message.Bytes.ToArray());
    }

    private void OnNetChatMessage(PlayerSession s, NetOpsSession state, byte[] message)
    {
        var options = _config.NetOps;
        if (!NetChatWire.TrySplit(message, out var kind, out var name, out var body)) { NetStats.Malformed++; return; }
        if (kind == NetChatWire.KindHello)
        {
            if (name != NetChatWire.HandshakeName || body.Length < 1) { NetStats.Malformed++; return; }
            state.Announced = true; state.ChatCapable = true; state.NextQueryMs = 0;   // the query goes out on the next tick
            return;
        }
        if (kind == NetChatWire.KindAnswer)
        {
            if (name != NetChatWire.HandshakeName || body.Length < 6) { NetStats.Malformed++; return; }
            var control = BinaryPrimitives.ReadUInt16LittleEndian(body[1..]); var data = BinaryPrimitives.ReadUInt16LittleEndian(body[3..]);
            state.ChatCapable = true; state.Answered = true;
            state.Agreed = (body[5] & 1) != 0 && control == options.Control && data == options.Data;
            Log.Info($"{s.Describe()} net channels: answer {(state.Agreed ? "accepts" : "declines")} custom types 0x{control:X}/0x{data:X} (chat carrier in use{(state.Agreed ? ", HELLO next" : "")})");
            return;
        }
        if (kind is not (NetChatWire.KindBytes or NetChatWire.KindJson)) { NetStats.Malformed++; return; }
        state.ChatCapable = true;
        if (body.Length > ChatMaxBody) { NetStats.TooLarge++; return; }
        var id = NetOpsWire.ChannelId(name);
        if (!state.Channels.Contains(id))
        {
            if (state.Channels.Count >= options.MaxChannelsPerClient) { NetStats.TooManyChannels++; return; }
            state.Channels.Add(id);
        }
        if (!_netChannels.TryGetValue(id, out var channel) || channel.Name != name) { NetStats.UnknownChannel++; return; }
        DeliverNet(s, channel, kind == NetChatWire.KindJson, body.ToArray());
    }

    /// <summary>
    /// A client's private chat to another player: relayed as chat always, except the frames of a net channel message that names a server-only
    /// channel (its first frame carries the name; the later frames of that message are dropped as well).
    /// </summary>
    private bool NetChatRelayAllowed(PlayerSession s, ReadOnlySpan<byte> text)
    {
        if (!_config.NetOps.Enabled || !NetChatWire.TryRead(text, out var frame)) return true;
        var state = NetStateOf(s);
        if (frame.Index != 0) return !state.BlockedChat.Contains(frame.Number);
        state.BlockedChat.Remove(frame.Number);
        var peek = new List<byte>();
        if (!NetChatWire.Unpack(frame.Packed, Math.Min(frame.Length, 2 + 64), peek) && peek.Count < 2) return true;
        if (!NetChatWire.TrySplit(peek.ToArray(), out var kind, out var name, out _) || kind is not (NetChatWire.KindBytes or NetChatWire.KindJson)) return true;
        if (!_netChannels.TryGetValue(NetOpsWire.ChannelId(name), out var channel) || channel.Name != name || !channel.ServerOnly) return true;
        NetStats.ServerOnlyRefused++;
        if (frame.Length > NetChatWire.FirstBytes) { if (state.BlockedChat.Count > 32) state.BlockedChat.Clear(); state.BlockedChat.Add(frame.Number); }
        return false;
    }

    // ---- sending: private chat from slot 0 -------------------------------------------------------------------------------

    /// <summary>Queues one message (kind, name, body) for the player over the chat carrier (paced by <see cref="PumpNetChat"/>).</summary>
    private bool QueueNetChat(PlayerSession to, byte kind, string name, ReadOnlySpan<byte> body, out string? error)
    {
        error = null;
        var state = NetStateOf(to);
        if (body.Length > ChatMaxBody) { error = "the message is larger than 64 KB on the chat carrier"; return false; }
        var message = NetChatWire.Join(kind, name, body);
        var frames = NetChatWire.Encode(message, (int)(state.NextChatNumber++ & 0x7FFF));
        var bytes = frames.Sum(f => f.Length);
        if (state.ChatOutBytes + bytes > ChatMaxQueued) { error = "too many messages are waiting to be sent"; return false; }
        foreach (var frame in frames) state.ChatOut.Enqueue(frame);
        state.ChatOutBytes += bytes;
        return true;
    }

    /// <summary>The query that starts the handshake: the numbers this server offers.</summary>
    private void SendNetQuery(PlayerSession to)
    {
        Span<byte> body = stackalloc byte[5];
        body[0] = NetOpsWire.ProtocolVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(body[1..], _config.NetOps.Control);
        BinaryPrimitives.WriteUInt16LittleEndian(body[3..], _config.NetOps.Data);
        QueueNetChat(to, NetChatWire.KindQuery, NetChatWire.HandshakeName, body, out _);
    }

    /// <summary>Every tick: queries for players that announced themselves (until they answer), then the paced chat frames.</summary>
    private void PumpNetChat(long now)
    {
        var options = _config.NetOps;
        Span<byte> payload = stackalloc byte[ChatPayloadBytes];
        foreach (var session in _sessions)
        {
            if (session.State < SessionState.SlotAssigned || session.State == SessionState.Closed || session.Connection.IsClosed) continue;
            var state = NetStateOf(session);
            if (state.Announced && !state.Answered && state.QueriesSent < ChatQueriesMax && now >= state.NextQueryMs)
            {
                state.QueriesSent++; state.NextQueryMs = now + ChatQueryEveryMs;
                SendNetQuery(session);
            }
            if (state.ChatIn.Count > 0)
                foreach (var (number, pending) in state.ChatIn.ToArray())
                    if (now - pending.LastMs > options.FragmentTimeoutSeconds * 1000L) { state.ReassemblyBytes -= pending.Bytes.Count; state.ChatIn.Remove(number); NetStats.TimedOut++; }
            if (state.ChatOut.Count == 0) { state.ChatCreditMs = now; continue; }
            state.ChatCredit = Math.Min(options.ChatBytesPerSecond, state.ChatCredit + (now - state.ChatCreditMs) * (options.ChatBytesPerSecond / 1000.0));
            state.ChatCreditMs = now;
            while (state.ChatOut.Count > 0 && state.ChatCredit >= 0)
            {
                var frame = state.ChatOut.Dequeue();
                state.ChatOutBytes -= frame.Length;
                state.ChatCredit -= frame.Length + 20;
                BuildChat(payload, session.Slot, frame);
                SendMessage(session, 0x003, 0, 0, payload);
                NetStats.ChatFramesSent++;
            }
        }
    }
}
