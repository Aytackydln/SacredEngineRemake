using System.Text.Json;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Gameplay;

internal static class ImmediateMessageDispatchChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-immediate-message-dispatch.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var count = row.GetProperty("count").GetInt32();
            var slots = row.GetProperty("slots").GetInt32();
            var mode = row.GetProperty("mode").GetString();
            var events = new List<string>();
            var message = new object();
            WorldImmediateMessageDispatcher<object>? dispatcher = null;
            Action<object> second = incoming => { Check(ReferenceEquals(message, incoming), "same message reaches second listener"); events.Add("second"); };
            Action<object> replacement = incoming => { Check(ReferenceEquals(message, incoming), "same message reaches replacement listener"); events.Add("replacement"); };
            var alternate = false;
            Action<object>?[] backing = [null, (slots & 2) != 0 ? second : null];
            Action<object> first = incoming =>
            {
                Check(ReferenceEquals(message, incoming), "same message reaches first listener");
                events.Add("first");
                switch (mode)
                {
                    case "clear": count = 0; break;
                    case "grow": count = 2; break;
                    case "replace": backing = [backing[0], replacement]; alternate = true; count = 2; break;
                    case "null-second": backing[1] = null; break;
                    case "reenter": dispatcher!.Dispatch(message, 2); break;
                }
            };
            backing[0] = (slots & 1) != 0 ? first : null;
            dispatcher = new(new(() => count, index => backing[index], _ =>
            {
                events.Add("family"); return row.GetProperty("family").GetUInt32();
            }, _ => events.Add("unsupported")), row.GetProperty("initial").GetUInt32());
            dispatcher.Dispatch(message, row.GetProperty("mask").GetUInt32());
            Check(events.SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetString()!)),
                "complete native routing, masks, zero slots and nested/current-storage order");
            Check(dispatcher.DirectCalls == row.GetProperty("direct").GetUInt32(), "native direct counter, recursion and wrap");
            Check(count == row.GetProperty("count_after").GetInt32(), "logical extent after listener reentry");
            Check(alternate == row.GetProperty("alternate").GetBoolean(), "current backing replacement");
            Check((backing[1] is not null) == row.GetProperty("second_present").GetBoolean(), "physical slot presence independent of logical extent");
        }
        foreach (var row in native.RootElement.GetProperty("constructors").EnumerateArray())
        {
            var events = new List<string>();
            var dispatcher = new WorldAdmissionUiDispatcher(() => 1, _ => _ => events.Add("first"));
            dispatcher.Dispatch(new(row.GetProperty("event").GetUInt32(), row.GetProperty("target").GetString()!, row.GetProperty("mode").GetUInt32(),
                row.GetProperty("target").GetString() == "UI_WND_NETWORK" ? row.GetProperty("word_10").GetUInt32() : row.GetProperty("word_c").GetUInt32(),
                Parameter: row.GetProperty("parameter").GetUInt32()));
            Check(row.GetProperty("family").GetUInt32() == 5 && dispatcher.DirectCalls == row.GetProperty("direct").GetUInt32() &&
                events.SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetString()!)),
                "actual UI constructor family getter and immediate delivery compose with the typed adapter");
        }
        return checks;
        void Check(bool pass, string why)
        { if (!pass) throw new Exception("Immediate message delivery: " + why); checks++; }
    }
}
