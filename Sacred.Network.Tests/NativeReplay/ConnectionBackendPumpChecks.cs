using System.Text.Json;
using Sacred.GameplayHost.Networking;

internal static class ConnectionBackendPumpChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-connection-backend-pump.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var a = new Backend("a", row.GetProperty("count").GetInt32(), row.GetProperty("values"));
            var secondary = row.GetProperty("secondary");
            var b = new Backend("b", secondary.GetArrayLength(), secondary);
            Backend? current = row.GetProperty("present").GetBoolean() ? a : null;
            var events = new List<string>();
            var linked = new List<uint>();
            var mode = row.GetProperty("mode").GetString();
            var appended = false;
            var services = new WorldConnectionBackendPumpServices<Backend>(() => current,
                backend =>
                {
                    events.Add("poll:" + backend.Name);
                    if (mode == "poll-replace") current = b;
                    else if (mode == "poll-grow") backend.Append(0x7777);
                },
                backend => events.Add("enter:" + backend.Name),
                backend =>
                {
                    events.Add("exit:" + backend.Name);
                    if (mode == "release-remove") current = null;
                    else if (mode == "release-replace") current = b;
                }, backend => backend.Count, (backend, index) => backend.Read(index),
                backend => backend.Count = 0,
                () => events.Add("enter:connection"), () => events.Add("exit:connection"),
                value =>
                {
                    events.Add("append:" + value);
                    linked.Add(value);
                    if (!appended)
                    {
                        if (mode == "append-grow") current!.Append(0x8888);
                        else if (mode == "append-edit-next" && current!.Count > 1) current.Write(1, 0x9999);
                        else if (mode == "append-replace") current = b;
                    }
                    appended = true;
                }, backend =>
                {
                    events.Add("query:" + backend.Name);
                    return (row.GetProperty("query").GetUInt32() & 255) != 0;
                });
            uint word30 = 0xdeadbeef, word34 = uint.MaxValue;
            var pending = new List<int> { 0, 1 };
            var result = row.GetProperty("entry").GetString() == "finalizer"
                ? WorldAdmissionFinalizer.Reset(true, pending, () => word30 = word34 = 0,
                    index =>
                    {
                        Check(word30 == 0 && word34 == 0, "controller words reset before destruction and pump");
                        events.Add("destroy:" + index);
                    }, () =>
                    {
                        Check(pending.Count == 0, "pending records cleared before actual pump/query");
                        return WorldConnectionBackendPump.PumpAndQuery(services);
                    })
                : WorldConnectionBackendPump.PumpAndQuery(services);
            Check(result == (row.GetProperty("result").GetUInt32() != 0), "native AL query normalization/absent backend");
            Check(events.SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetString()!)),
                "poll/lock/append/unlock/query order and current backend identity");
            Check(linked.SequenceEqual(row.GetProperty("linked").EnumerateArray().Select(value => value.GetUInt32())),
                "actual native doubly linked insertion order and null-pointer skipping");
            Check(a.Count == row.GetProperty("count_a").GetInt32() && b.Count == row.GetProperty("count_b").GetInt32(),
                "captured iteration extent versus fresh complete clear extent");
            Check(current?.Name == row.GetProperty("backend").GetString(), "backend replacement/removal across service callbacks");
            Check(word30 == row.GetProperty("word30").GetUInt32() && word34 == row.GetProperty("word34").GetUInt32() &&
                pending.Count == row.GetProperty("pending").GetInt32(), "actual finalizer composition versus direct pump");
        }
        CheckMissingLifetime();
        return checks;

        void Check(bool pass, string why)
        { if (!pass) throw new Exception("Connection backend pump: " + why); checks++; }

        void CheckMissingLifetime()
        {
            var values = JsonDocument.Parse("[]");
            var backend = new Backend("a", 0, values.RootElement);
            Backend? current = backend;
            var services = new WorldConnectionBackendPumpServices<Backend>(() => current, _ => current = null,
                _ => throw new Exception("must guard before lock"), _ => { }, _ => 0, (_, _) => 0,
                _ => { }, () => { }, () => { }, _ => { }, _ => false);
            try { WorldConnectionBackendPump.Pump(services); throw new Exception("missing backend accepted"); }
            catch (NotSupportedException) { checks++; }
            values.Dispose();
        }
    }

    private sealed class Backend
    {
        // Explicit zeroed original backing includes one word before begin;
        // the negative-extent poll control writes it before restoring length zero.
        private readonly uint[] _backing = new uint[66];
        public Backend(string name, int count, JsonElement values)
        {
            Name = name; Count = count;
            var index = 0;
            foreach (var value in values.EnumerateArray()) Write(index++, value.GetUInt32());
        }
        public string Name { get; }
        public int Count { get; set; }
        public uint Read(int index) => _backing[index + 1];
        public void Write(int index, uint value) => _backing[index + 1] = value;
        public void Append(uint value) { Write(Count, value); Count++; }
    }
}
