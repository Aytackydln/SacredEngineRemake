using System.Buffers.Binary;

namespace Sacred.GameServer.Net.Kcp;

/// <summary>
/// Managed port of KCP (ikcp.c by Lin Wei / skywind3000, MIT licence, https://github.com/skywind3000/kcp), the ARQ protocol
/// Sacred Bild runs its LAN streams on. Wire-compatible with the C library (24-byte little-endian segment header:
/// conv u32, cmd u8, frg u8, wnd u16, ts u32, sn u32, una u32, len u32). Differences from the C code are structural only:
/// segments and their data buffers are pooled and reused, the queues are lists, and output goes into one reusable buffer
/// with <see cref="Reserved"/> leading bytes left free for the caller's datagram header (no copy).
/// Not thread-safe: one owner thread calls everything.
/// </summary>
public sealed class Kcp
{
    public const int Overhead = 24;
    private const uint CmdPush = 81, CmdAck = 82, CmdWask = 83, CmdWins = 84;
    private const uint AskSend = 1, AskTell = 2;
    private const uint RtoNoDelay = 30, RtoMin = 100, RtoDefault = 200, RtoMax = 60000;
    private const uint ThreshInit = 2, ThreshMin = 2, ProbeInit = 7000, ProbeLimit = 120000, FastAckLimit = 5;

    /// <summary>Called with the output buffer; the datagram is buffer[0..length), of which the first <see cref="Reserved"/>
    /// bytes belong to the caller.</summary>
    public delegate void OutputHandler(byte[] buffer, int length);

    private sealed class Segment
    {
        public uint Conv, Cmd, Frg, Wnd, Ts, Sn, Una, ResendTs, Rto, FastAck, Xmit;
        public byte[] Data = [];
        public int Length;
    }

    private readonly uint _conv;
    private readonly OutputHandler _output;
    private readonly Stack<Segment> _pool = new();
    private readonly List<Segment> _sndQueue = new(), _sndBuf = new(), _rcvQueue = new(), _rcvBuf = new();
    private uint[] _acks = new uint[64];
    private int _ackCount;
    private byte[] _buffer;
    private int _mtu, _mss;
    private uint _sndUna, _sndNxt, _rcvNxt, _ssthresh = ThreshInit;
    private int _rxRttval, _rxSrtt;
    private uint _rxRto = RtoDefault, _rxMinRto = RtoMin;
    private uint _sndWnd = 32, _rcvWnd = 128, _rmtWnd = 128, _cwnd, _incr, _probe, _current, _interval = 100, _tsFlush = 100;
    private uint _tsProbe, _probeWait;
    private bool _nodelay, _updated, _noCongestion;
    private int _fastResend, _fastLimit = (int)FastAckLimit;
    private uint _deadLink = 20;

    public int Reserved { get; }
    public bool Stream { get; set; }
    public uint State { get; private set; }
    public uint Conv => _conv;
    public int Mss => _mss;

    public Kcp(uint conv, OutputHandler output, int reserved = 0)
    {
        _conv = conv; _output = output; Reserved = reserved;
        _mtu = 1400; _mss = _mtu - Overhead;
        _buffer = new byte[reserved + (_mtu + Overhead) * 3];
    }

    public uint DeadLink { get => _deadLink; set => _deadLink = value; }
    public int WaitSnd => _sndBuf.Count + _sndQueue.Count;

    public void SetMtu(int mtu)
    {
        if (mtu < 50 || mtu < Overhead) throw new ArgumentOutOfRangeException(nameof(mtu));
        _buffer = new byte[Reserved + (mtu + Overhead) * 3];
        _mtu = mtu; _mss = mtu - Overhead;
    }

    public void WndSize(int send, int receive)
    {
        if (send > 0) _sndWnd = (uint)send;
        if (receive > 0) _rcvWnd = (uint)Math.Max(receive, 128);   // as ikcp_wndsize: at least IKCP_WND_RCV
    }

    public void NoDelay(int nodelay, int interval, int resend, int nc)
    {
        if (nodelay >= 0) { _nodelay = nodelay != 0; _rxMinRto = _nodelay ? RtoNoDelay : RtoMin; }
        if (interval >= 0) _interval = (uint)Math.Clamp(interval, 10, 5000);
        if (resend >= 0) _fastResend = resend;
        if (nc >= 0) _noCongestion = nc != 0;
    }

    private Segment NewSegment(int size)
    {
        var s = _pool.Count > 0 ? _pool.Pop() : new Segment();
        if (s.Data.Length < size) s.Data = new byte[Math.Max(size, _mss)];
        s.Length = 0; s.Conv = s.Cmd = s.Frg = s.Wnd = s.Ts = s.Sn = s.Una = s.ResendTs = s.Rto = s.FastAck = s.Xmit = 0;
        return s;
    }

    private void Free(Segment s) => _pool.Push(s);

    // ---- user side ----

    /// <summary>Size of the next complete message, or -1.</summary>
    public int PeekSize()
    {
        if (_rcvQueue.Count == 0) return -1;
        var first = _rcvQueue[0];
        if (first.Frg == 0) return first.Length;
        if (_rcvQueue.Count < first.Frg + 1) return -1;
        var length = 0;
        foreach (var s in _rcvQueue) { length += s.Length; if (s.Frg == 0) break; }
        return length;
    }

    /// <summary>Copies the next message into <paramref name="destination"/>; returns its length, or a negative value.</summary>
    public int Recv(Span<byte> destination)
    {
        if (_rcvQueue.Count == 0) return -1;
        var peek = PeekSize();
        if (peek < 0) return -2;
        if (peek > destination.Length) return -3;
        var recover = _rcvQueue.Count >= _rcvWnd;
        var length = 0;
        var taken = 0;
        while (taken < _rcvQueue.Count)
        {
            var s = _rcvQueue[taken++];
            s.Data.AsSpan(0, s.Length).CopyTo(destination[length..]);
            length += s.Length;
            var fragment = s.Frg;
            Free(s);
            if (fragment == 0) break;
        }
        _rcvQueue.RemoveRange(0, taken);
        MoveReceiveBuffer();
        if (_rcvQueue.Count < _rcvWnd && recover) _probe |= AskTell;
        return length;
    }

    private void MoveReceiveBuffer()
    {
        var moved = 0;
        while (moved < _rcvBuf.Count)
        {
            var s = _rcvBuf[moved];
            if (s.Sn != _rcvNxt || _rcvQueue.Count >= _rcvWnd) break;
            _rcvQueue.Add(s); _rcvNxt++; moved++;
        }
        if (moved > 0) _rcvBuf.RemoveRange(0, moved);
    }

    /// <summary>Queues user data; returns 0, or a negative value when it does not fit the window rules of ikcp_send.</summary>
    public int Send(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0) return -1;
        if (Stream && _sndQueue.Count > 0)
        {
            var last = _sndQueue[^1];
            if (last.Length < _mss)
            {
                var extend = Math.Min(data.Length, _mss - last.Length);
                data[..extend].CopyTo(last.Data.AsSpan(last.Length));
                last.Length += extend; last.Frg = 0;
                data = data[extend..];
            }
            if (data.Length == 0) return 0;
        }
        var count = data.Length <= _mss ? 1 : (data.Length + _mss - 1) / _mss;
        if (count >= 128) return -2;                               // IKCP_WND_RCV: callers split larger messages
        if (count == 0) count = 1;
        for (var i = 0; i < count; i++)
        {
            var size = Math.Min(data.Length, _mss);
            var s = NewSegment(_mss);
            data[..size].CopyTo(s.Data);
            s.Length = size;
            s.Frg = Stream ? 0u : (uint)(count - i - 1);
            _sndQueue.Add(s);
            data = data[size..];
        }
        return 0;
    }

    // ---- network side ----

    private void UpdateAck(int rtt)
    {
        if (_rxSrtt == 0) { _rxSrtt = rtt; _rxRttval = rtt / 2; }
        else
        {
            var delta = Math.Abs(rtt - _rxSrtt);
            _rxRttval = (3 * _rxRttval + delta) / 4;
            _rxSrtt = (7 * _rxSrtt + rtt) / 8;
            if (_rxSrtt < 1) _rxSrtt = 1;
        }
        var rto = (uint)(_rxSrtt + Math.Max((int)_interval, 4 * _rxRttval));
        _rxRto = Math.Clamp(rto, _rxMinRto, RtoMax);
    }

    private void ShrinkBuffer() => _sndUna = _sndBuf.Count > 0 ? _sndBuf[0].Sn : _sndNxt;

    private void ParseAck(uint sn)
    {
        if (Diff(sn, _sndUna) < 0 || Diff(sn, _sndNxt) >= 0) return;
        for (var i = 0; i < _sndBuf.Count; i++)
        {
            var s = _sndBuf[i];
            if (sn == s.Sn) { _sndBuf.RemoveAt(i); Free(s); break; }
            if (Diff(sn, s.Sn) < 0) break;
        }
    }

    private void ParseUna(uint una)
    {
        var removed = 0;
        while (removed < _sndBuf.Count && Diff(una, _sndBuf[removed].Sn) > 0) Free(_sndBuf[removed++]);
        if (removed > 0) _sndBuf.RemoveRange(0, removed);
    }

    private void ParseFastAck(uint sn, uint ts)
    {
        if (Diff(sn, _sndUna) < 0 || Diff(sn, _sndNxt) >= 0) return;
        foreach (var s in _sndBuf)
        {
            if (Diff(sn, s.Sn) < 0) break;
            if (sn != s.Sn) s.FastAck++;      // IKCP_FASTACK_CONSERVE not defined in the C default build
        }
    }

    private void PushAck(uint sn, uint ts)
    {
        if ((_ackCount + 1) * 2 > _acks.Length) Array.Resize(ref _acks, _acks.Length * 2);
        _acks[_ackCount * 2] = sn; _acks[_ackCount * 2 + 1] = ts; _ackCount++;
    }

    private void ParseData(Segment segment)
    {
        var sn = segment.Sn;
        if (Diff(sn, _rcvNxt + _rcvWnd) >= 0 || Diff(sn, _rcvNxt) < 0) { Free(segment); return; }
        var insertAt = _rcvBuf.Count;
        var repeat = false;
        for (var i = _rcvBuf.Count - 1; i >= 0; i--)
        {
            var s = _rcvBuf[i];
            if (s.Sn == sn) { repeat = true; break; }
            if (Diff(sn, s.Sn) > 0) { insertAt = i + 1; break; }
            insertAt = i;
        }
        if (repeat) Free(segment); else _rcvBuf.Insert(insertAt, segment);
        MoveReceiveBuffer();
    }

    /// <summary>Feeds one received KCP packet (possibly several segments). Returns 0 or a negative error as ikcp_input.</summary>
    public int Input(ReadOnlySpan<byte> data)
    {
        var prevUna = _sndUna;
        uint maxAck = 0, latestTs = 0;
        var flag = false;
        if (data.Length < Overhead) return -1;
        while (data.Length >= Overhead)
        {
            var conv = BinaryPrimitives.ReadUInt32LittleEndian(data);
            if (conv != _conv) return -1;
            uint cmd = data[4], frg = data[5];
            uint wnd = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
            var ts = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
            var sn = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
            var una = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
            var len = BinaryPrimitives.ReadUInt32LittleEndian(data[20..]);
            data = data[Overhead..];
            if (data.Length < len || len > int.MaxValue) return -2;
            if (cmd is not (CmdPush or CmdAck or CmdWask or CmdWins)) return -3;
            _rmtWnd = wnd;
            ParseUna(una);
            ShrinkBuffer();
            switch (cmd)
            {
                case CmdAck:
                    if (Diff(_current, ts) >= 0) UpdateAck(Diff(_current, ts));
                    ParseAck(sn);
                    ShrinkBuffer();
                    if (!flag) { flag = true; maxAck = sn; latestTs = ts; }
                    else if (Diff(sn, maxAck) > 0) { maxAck = sn; latestTs = ts; }
                    break;
                case CmdPush:
                    if (Diff(sn, _rcvNxt + _rcvWnd) < 0)
                    {
                        PushAck(sn, ts);
                        if (Diff(sn, _rcvNxt) >= 0)
                        {
                            if (len > 0x10000) return -2;                       // bound per-segment memory
                            var s = NewSegment((int)len);
                            s.Conv = conv; s.Cmd = cmd; s.Frg = frg; s.Wnd = wnd; s.Ts = ts; s.Sn = sn; s.Una = una;
                            data[..(int)len].CopyTo(s.Data);
                            s.Length = (int)len;
                            ParseData(s);
                        }
                    }
                    break;
                case CmdWask: _probe |= AskTell; break;
                case CmdWins: break;
            }
            data = data[(int)len..];
        }
        if (flag) ParseFastAck(maxAck, latestTs);
        if (Diff(_sndUna, prevUna) > 0 && _cwnd < _rmtWnd)
        {
            var mss = (uint)_mss;
            if (_cwnd < _ssthresh) { _cwnd++; _incr += mss; }
            else
            {
                if (_incr < mss) _incr = mss;
                _incr += mss * mss / _incr + mss / 16;
                if ((_cwnd + 1) * mss <= _incr) _cwnd = (_incr + mss - 1) / (mss > 0 ? mss : 1);
            }
            if (_cwnd > _rmtWnd) { _cwnd = _rmtWnd; _incr = _rmtWnd * mss; }
        }
        return 0;
    }

    private uint WndUnused() => _rcvQueue.Count < _rcvWnd ? _rcvWnd - (uint)_rcvQueue.Count : 0;

    private static int Encode(Span<byte> d, uint conv, uint cmd, uint frg, uint wnd, uint ts, uint sn, uint una, int len)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(d, conv);
        d[4] = (byte)cmd; d[5] = (byte)frg;
        BinaryPrimitives.WriteUInt16LittleEndian(d[6..], (ushort)wnd);
        BinaryPrimitives.WriteUInt32LittleEndian(d[8..], ts);
        BinaryPrimitives.WriteUInt32LittleEndian(d[12..], sn);
        BinaryPrimitives.WriteUInt32LittleEndian(d[16..], una);
        BinaryPrimitives.WriteUInt32LittleEndian(d[20..], (uint)len);
        return Overhead;
    }

    private int _fill;

    private void EmitIfFull(int needed)
    {
        if (_fill + needed > Reserved + _mtu) { _output(_buffer, _fill); _fill = Reserved; }
    }

    /// <summary>ikcp_flush: acks, window probes, new and resent segments.</summary>
    public void Flush()
    {
        if (!_updated) return;
        var current = _current;
        var wnd = WndUnused();
        _fill = Reserved;

        for (var i = 0; i < _ackCount; i++)
        {
            EmitIfFull(Overhead);
            _fill += Encode(_buffer.AsSpan(_fill), _conv, CmdAck, 0, wnd, _acks[i * 2 + 1], _acks[i * 2], _rcvNxt, 0);
        }
        _ackCount = 0;

        if (_rmtWnd == 0)
        {
            if (_probeWait == 0) { _probeWait = ProbeInit; _tsProbe = _current + _probeWait; }
            else if (Diff(_current, _tsProbe) >= 0)
            {
                if (_probeWait < ProbeInit) _probeWait = ProbeInit;
                _probeWait += _probeWait / 2;
                if (_probeWait > ProbeLimit) _probeWait = ProbeLimit;
                _tsProbe = _current + _probeWait;
                _probe |= AskSend;
            }
        }
        else { _tsProbe = 0; _probeWait = 0; }

        if ((_probe & AskSend) != 0) { EmitIfFull(Overhead); _fill += Encode(_buffer.AsSpan(_fill), _conv, CmdWask, 0, wnd, 0, 0, _rcvNxt, 0); }
        if ((_probe & AskTell) != 0) { EmitIfFull(Overhead); _fill += Encode(_buffer.AsSpan(_fill), _conv, CmdWins, 0, wnd, 0, 0, _rcvNxt, 0); }
        _probe = 0;

        var cwnd = Math.Min(_sndWnd, _rmtWnd);
        if (!_noCongestion) cwnd = Math.Min(_cwnd, cwnd);

        while (Diff(_sndNxt, _sndUna + cwnd) < 0 && _sndQueue.Count > 0)
        {
            var s = _sndQueue[0];
            _sndQueue.RemoveAt(0);
            s.Conv = _conv; s.Cmd = CmdPush; s.Wnd = wnd; s.Ts = current; s.Sn = _sndNxt++; s.Una = _rcvNxt;
            s.ResendTs = current; s.Rto = _rxRto; s.FastAck = 0; s.Xmit = 0;
            _sndBuf.Add(s);
        }

        var resent = _fastResend > 0 ? (uint)_fastResend : uint.MaxValue;
        var rtoMin = _nodelay ? 0 : _rxRto >> 3;
        bool lost = false, change = false;
        foreach (var s in _sndBuf)
        {
            var needSend = false;
            if (s.Xmit == 0) { needSend = true; s.Xmit++; s.Rto = _rxRto; s.ResendTs = current + s.Rto + rtoMin; }
            else if (Diff(current, s.ResendTs) >= 0)
            {
                needSend = true; s.Xmit++;
                if (!_nodelay) s.Rto += Math.Max(s.Rto, _rxRto);
                else s.Rto += _nodelay ? s.Rto / 2 : _rxRto / 2;      // ikcp: nodelay < 2 -> rto/2
                s.ResendTs = current + s.Rto;
                lost = true;
            }
            else if (s.FastAck >= resent && (s.Xmit <= _fastLimit || _fastLimit <= 0))
            {
                needSend = true; s.Xmit++; s.FastAck = 0; s.ResendTs = current + s.Rto; change = true;
            }
            if (!needSend) continue;
            s.Ts = current; s.Wnd = wnd; s.Una = _rcvNxt;
            EmitIfFull(Overhead + s.Length);
            _fill += Encode(_buffer.AsSpan(_fill), s.Conv, s.Cmd, s.Frg, s.Wnd, s.Ts, s.Sn, s.Una, s.Length);
            s.Data.AsSpan(0, s.Length).CopyTo(_buffer.AsSpan(_fill));
            _fill += s.Length;
            if (s.Xmit >= _deadLink) State = uint.MaxValue;
        }
        if (_fill > Reserved) { _output(_buffer, _fill); _fill = Reserved; }

        if (change)
        {
            var inflight = _sndNxt - _sndUna;
            _ssthresh = Math.Max(inflight / 2, ThreshMin);
            _cwnd = _ssthresh + resent; _incr = _cwnd * (uint)_mss;
        }
        if (lost) { _ssthresh = Math.Max(cwnd / 2, ThreshMin); _cwnd = 1; _incr = (uint)_mss; }
        if (_cwnd < 1) { _cwnd = 1; _incr = (uint)_mss; }
    }

    /// <summary>Sacred Bild's silence rule: when the peer is heard again after an outage, every unacknowledged segment goes
    /// out with the next flush instead of after KCP's back-off.</summary>
    public void ResendNow()
    {
        foreach (var s in _sndBuf) if (s.Xmit > 0) s.ResendTs = _current;
    }

    /// <summary>ikcp_update with a millisecond clock (wraps like the C uint32 clock).</summary>
    public void Update(uint current)
    {
        _current = current;
        if (!_updated) { _updated = true; _tsFlush = _current; }
        var slap = Diff(_current, _tsFlush);
        if (slap >= 10000 || slap < -10000) { _tsFlush = _current; slap = 0; }
        if (slap >= 0)
        {
            _tsFlush += _interval;
            if (Diff(_current, _tsFlush) >= 0) _tsFlush = _current + _interval;
            Flush();
        }
    }

    /// <summary>ikcp_check: when <see cref="Update"/> should run next.</summary>
    public uint Check(uint current)
    {
        if (!_updated) return current;
        var tsFlush = _tsFlush;
        if (Diff(current, tsFlush) >= 10000 || Diff(current, tsFlush) < -10000) tsFlush = current;
        if (Diff(current, tsFlush) >= 0) return current;
        var tmFlush = Diff(tsFlush, current);
        var tmPacket = int.MaxValue;
        foreach (var s in _sndBuf)
        {
            var diff = Diff(s.ResendTs, current);
            if (diff <= 0) return current;
            if (diff < tmPacket) tmPacket = diff;
        }
        var minimal = (uint)Math.Min(Math.Min(tmPacket, tmFlush), (int)_interval);
        return current + minimal;
    }

    private static int Diff(uint later, uint earlier) => (int)(later - earlier);
}
