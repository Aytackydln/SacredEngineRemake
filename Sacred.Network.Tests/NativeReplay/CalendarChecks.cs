using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;

internal static class CalendarChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool test, string name) { if (!test) throw new Exception(name); checks++; }
        void Reject(Action action) { try { action(); } catch (InvalidDataException) { checks++; return; } throw new Exception("Malformed environment accepted"); }
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-calendar.json")));
        var clock = new TestTime();
        var replica = new GoldCalendarReplica(clock);
        foreach (var sample in native.RootElement.GetProperty("samples").EnumerateArray())
        {
            var bytes = Convert.FromHexString(sample.GetProperty("payload_hex").GetString()!);
            var fields = sample.GetProperty("fields").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            var payload = new byte[508]; payload[0] = 255; bytes.CopyTo(payload, 4);
            var environment = (GoldWorldEnvironmentEvent)GoldGameEventDecoder.Decode(new(0xB, 0, 0, 0, payload));
            var calendar = environment.Calendar!;
            Check(new int[] { calendar.Year, calendar.Month, calendar.Day, calendar.Hours, calendar.Minutes, calendar.Seconds }.SequenceEqual(fields), "Native packed calendar fields");
            Check(calendar.ToPayload().SequenceEqual(bytes), "Native packed calendar export");
            replica.Apply(environment);
            var seconds = fields[3] * 3600 + fields[4] * 60 + fields[5];
            Check(Math.Abs(replica.DayFraction!.Value * 86400 - seconds) < .01, "Received calendar fraction");
            clock.Advance(2.5);
            Check(Math.Abs(replica.DayFraction!.Value * 86400 - (seconds + 60) % 86400) < .01, "Default native presentation rate");
        }
        Check(((GoldWorldEnvironmentEvent)GoldGameEventDecoder.Decode(new(0xB, 2, 2, 0, []))).IsRequest, "Empty client environment request");
        foreach (var length in new[] { 0, 1, 507, 509 }) Reject(() => GoldGameEventDecoder.Decode(new(0xB, 0, 0, 0, new byte[length])));
        Reject(() => GoldGameEventDecoder.Decode(new(0xB, 2, 2, 0, new byte[508])));
        var absent = new GoldWorldEnvironmentEvent(new(0xB, 0, 0, 0, new byte[508]));
        var current = replica.Current;
        replica.Apply(absent);
        Check(ReferenceEquals(current, replica.Current), "Absent calendar flag retains previous clock");
        var invalid = new byte[508]; invalid[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(invalid.AsSpan(12), ushort.MaxValue);
        replica.Apply((GoldWorldEnvironmentEvent)GoldGameEventDecoder.Decode(new(0xB, 0, 0, 0, invalid)));
        Check(replica.DayFraction is null && replica.Current?.Hours == ushort.MaxValue, "Invalid time stays raw without corrupting rendering");
        var world = new GoldWorldReplica(); var delivered = 0;
        Reject(() => GoldGameDispatcher.Dispatch([absent.Message, new(0xB, 0, 0, 0, [1])], e => { world.Apply(e); delivered++; }));
        Check(delivered == 0 && world.Calendar.Current is null, "Malformed environment batch is atomic");
        var valid = new byte[508]; valid[0] = 1; valid[1] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(valid.AsSpan(12), 23);
        BinaryPrimitives.WriteUInt16LittleEndian(valid.AsSpan(14), 59);
        BinaryPrimitives.WriteUInt16LittleEndian(valid.AsSpan(16), 59);
        var decoded = (GoldWorldEnvironmentEvent)GoldGameEventDecoder.Decode(new(0xB, 0, 1, 0, valid));
        valid[12] = 12;
        Check(decoded.Calendar!.Hours == 23 && decoded.Weather.Length == 32, "Decoder owns original buffer, retains weather");
        replica.Apply(decoded); clock.Advance(1.0 / 24 - .000001);
        Check(replica.DayFraction is >= 0 and < 1, "Float rounding at midnight remains a valid render fraction");
        clock.Advance(1);
        Check(replica.DayFraction < .001, "Interpolation wraps midnight");
        world.Apply(decoded); world.Clear(); replica.Clear();
        Check(world.Calendar.Current is null && replica.DayFraction is null, "Disconnect clears calendar");
        Console.WriteLine($"PASS: {checks} native calendar parity, interpolation, malformed batch and session cleanup checks.");
        return checks;
    }
    private sealed class TestTime : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 10000000;
        public override long GetTimestamp() => _timestamp;
        public void Advance(double seconds) => _timestamp += (long)(seconds * TimestampFrequency);
    }
}
