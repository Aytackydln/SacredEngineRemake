using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.Network.Connection;

/// <summary>Bounded protocol metadata for callback/disconnect investigation. Never retains character payloads.</summary>
public sealed class GoldConnectionDiagnostics
{
    private readonly object _gate = new();
    private readonly Queue<GoldMessageTrace> _recent = new();
    private long _received, _sent, _probes, _replies, _pulses;
    public long ReceivedMessages { get { lock (_gate) return _received; } }
    public long SentMessages { get { lock (_gate) return _sent; } }
    public long TimeProbesReceived { get { lock (_gate) return _probes; } }
    public long TimeRepliesSent { get { lock (_gate) return _replies; } }
    public long GameplayPulsesSent { get { lock (_gate) return _pulses; } }
    public IReadOnlyList<GoldMessageTrace> Recent { get { lock (_gate) return _recent.ToArray(); } }

    internal void Record(GoldMessage message, bool outgoing, long milliseconds)
    {
        lock (_gate)
        {
            if (outgoing)
            {
                _sent++;
                if (message.Opcode == GoldTimeProbeCodec.ReplyOpcode) _replies++;
                if (message.Opcode == GoldWorldEnvironmentLayout.Opcode && message.Payload.Length == 0) _pulses++;
            }
            else
            { _received++; if (message.Opcode == GoldTimeProbeCodec.RequestOpcode) _probes++; }
            if (_recent.Count == 64) _recent.Dequeue();
            _recent.Enqueue(new(milliseconds, outgoing, message.Opcode, message.Sequence, message.Sender, message.Origin, message.Payload.Length));
        }
    }
}

public sealed record GoldMessageTrace(long Milliseconds, bool Outgoing, ushort Opcode, ushort Sequence,
    byte Sender, byte Origin, int PayloadBytes);
