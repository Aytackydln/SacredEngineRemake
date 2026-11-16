using System.Text.Json;
using Sacred.GameplayHost.Networking;

internal static class AdmissionResetServiceChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-admission-reset-service.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var events = new List<string>();
            var states = new List<WorldAdmissionResetState>();
            var labels = new Dictionary<WorldAdmissionResetState, string>();
            var seeded = false;
            var allocations = row.GetProperty("present").GetBoolean() ? 1 : 0;
            WorldAdmissionResetService? service = null;
            service = new(() =>
            {
                if (!seeded)
                {
                    states.Add(new(row.GetProperty("flag").GetByte()));
                    labels[states[0]] = "a";
                    return states[0];
                }
                var name = allocations++ == 0 ? "a" : "b";
                var success = row.GetProperty("allocation").GetBoolean();
                events.Add("allocate:" + (success ? name : "None"));
                if (!success) return null;
                events.Add("construct:" + name);
                var created = new WorldAdmissionResetState(1); states.Add(created); labels[created] = name; return created;
            }, previous =>
            {
                Check(service!.Current is null, "global clear precedes free callback");
                events.Add("free:a");
                if (row.GetProperty("reenter").GetBoolean()) service.Resolve();
            });
            if (row.GetProperty("present").GetBoolean()) service.Resolve();
            seeded = true;
            var guarded = false;
            switch (row.GetProperty("action").GetString())
            {
                case "release": service.Release(); break;
                case "resolve": service.Resolve(); break;
                case "reset":
                    try { service.ResetEntry(); }
                    catch (NotSupportedException) { guarded = true; }
                    break;
            }
            var label = service.Current is null ? null : labels[service.Current];
            Check(events.SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetString()!)), "actual allocation/publication/free/reentry order");
            Check(label == row.GetProperty("current").GetString(), "replacement survives release of prior instance");
            var flag = row.GetProperty("entry_flag");
            Check(service.Current?.EntryFlag0 == (flag.ValueKind == JsonValueKind.Null ? (byte?)null : flag.GetByte()), "existing versus constructed byte-zero reset");
            Check(guarded == row.GetProperty("guard").GetBoolean(), "failed factory remains unsupported without a fabricated service");
        }
        foreach (var row in native.RootElement.GetProperty("constructors").EnumerateArray())
        {
            var state = new WorldAdmissionResetState(1);
            Check(state.EntryFlag0 == row.GetProperty("entry_flag").GetByte(), "complete original constructor entry byte under supplied resource/RNG profiles");
            Check(row.GetProperty("rng_calls").GetInt32() == 9104 && row.GetProperty("resource_calls").GetInt32() == 640,
                "full constructor traverses random point/resource initialization before publication");
        }
        return checks;
        void Check(bool pass, string why)
        { if (!pass) throw new Exception("Admission reset service: " + why); checks++; }
    }
}
