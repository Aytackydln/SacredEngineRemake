using System.Buffers.Binary;
using Sacred.Core.Pak.Creature;

namespace Sacred.Network.Gameplay;

/// <summary>Received path playback from Gold 0x5953EB..0x59579B. Owns a private
/// path copy; never changes authoritative anchors or runs collision/AI callbacks.</summary>
public sealed class GoldMovementPathPlayback
{
    private GoldMovementPoint[] _points = [];
    private int _index, _speed, _fraction, _remainderX, _remainderY;
    public GoldMovementPoint Position { get; private set; }
    public bool Moving => _speed > 0 && _index >= 0 && _index < _points.Length - 1;
    public int Speed => _speed;
    /// <summary>Unquantized segment vector. Frame-sized integer steps are not a heading.</summary>
    public GoldMovementPoint Direction { get; private set; }

    /// <summary>Set the reported position without replacing path points, index or fractional clocks.</summary>
    public void ApplyCorrection(GoldMovementPoint position) => Position = position;

    public void ReadBank(ReadOnlySpan<byte> bank, GoldMovementPoint position)
    {
        if (bank.Length != GoldMovementLayout.ActionSize) throw new ArgumentException("Complete movement bank required.", nameof(bank));
        var count = BinaryPrimitives.ReadInt32LittleEndian(bank[GoldMovementPathLayout.CountOffset..]);
        var index = BinaryPrimitives.ReadInt32LittleEndian(bank[GoldMovementPathLayout.IndexOffset..]);
        _points = count is >= 0 and <= GoldMovementPathLayout.Capacity ? new GoldMovementPoint[count] : [];
        for (var i = 0; i < _points.Length; i++)
            _points[i] = new(BinaryPrimitives.ReadInt32LittleEndian(bank[(i * 8)..]), BinaryPrimitives.ReadInt32LittleEndian(bank[(i * 8 + 4)..]));
        _index = index;
        _speed = BinaryPrimitives.ReadInt32LittleEndian(bank[GoldMovementPathLayout.SpeedOffset..]);
        _fraction = BinaryPrimitives.ReadInt32LittleEndian(bank[GoldMovementPathLayout.DistanceFractionOffset..]);
        _remainderX = BinaryPrimitives.ReadInt32LittleEndian(bank[GoldMovementPathLayout.RemainderXOffset..]);
        _remainderY = BinaryPrimitives.ReadInt32LittleEndian(bank[GoldMovementPathLayout.RemainderYOffset..]);
        Position = position;
        SelectDirection();
    }

    /// <summary>0x161 replaces only the route/index. Fractional progress survives route refreshes.</summary>
    public void ReadPath(ReadOnlySpan<GoldMovementPoint> path, uint index, GoldMovementPoint position, int? speed = null)
    {
        _points = path.Length <= GoldMovementPathLayout.Capacity ? path.ToArray() : [];
        _index = index <= int.MaxValue ? (int)index : -1;
        if (speed is { } value) _speed = value;
        // Native event 0x201 overwrites point zero with the actor's current anchor.
        if (_points.Length > 0) _points[0] = position;
        Position = position;
        SelectDirection();
    }

    private void SelectDirection()
    {
        Direction = default;
        if (!Moving) return;
        // Duplicate points are skipped by Advance too; they cannot supply a facing.
        for (var i = _index; i + 1 < _points.Length; i++)
        {
            var first = _points[i]; var next = _points[i + 1];
            if (first == next) continue;
            Direction = new(unchecked(next.X - first.X), unchecked(next.Y - first.Y));
            return;
        }
    }

    public GoldMovementPoint Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!Moving || seconds == 0) return Position;
        // Native rendering caps a delayed tick to 300 ms, then accumulates Q16 distance.
        var fixedSpeed = unchecked(_speed << 16);
        _fraction = unchecked(_fraction + (int)(fixedSpeed * Math.Min(seconds, .3f)));
        if (_fraction < 0) { _fraction = 0; return Position; }
        var remaining = _fraction >> 16;
        _fraction &= 0xFFFF;
        if (remaining == 0) return Position;
        while (_index + 1 < _points.Length)
        {
            var first = _points[_index];
            var target = _points[_index + 1];
            var distance = SacredNpcDistance.Between(first.X, first.Y, target.X, target.Y);
            if (distance == 0)
            {
                if (_index + 2 < _points.Length) { _index++; SelectDirection(); continue; }
                _points = [];
                return Position;
            }
            if (remaining > distance)
            {
                remaining -= (int)distance;
                Position = target;
                _index++;
                SelectDirection();
                if (_index + 1 == _points.Length) return Position;
                continue;
            }
            var dx = unchecked(target.X - first.X);
            var dy = unchecked(target.Y - first.Y);
            _remainderX = unchecked(_remainderX + unchecked(remaining * dx << 10) / (int)distance);
            _remainderY = unchecked(_remainderY + unchecked(remaining * dy << 10) / (int)distance);
            var x = _remainderX / 1024;
            var y = _remainderY / 1024;
            _remainderX -= x * 1024;
            _remainderY -= y * 1024;
            Position = new(unchecked(first.X + x), unchecked(first.Y + y));
            _points[_index] = Position;
            return Position;
        }
        return Position;
    }
}
