using System.Diagnostics;
using System.Text.Json;
using Sacred.GameServer.Scripting;
using Sacred.GameServer.Web;

namespace Sacred.GameServer.Tests;

/// <summary>The hooks part of the Events editor page: what the page carries, and (with node, when installed) the editor's own code that turns the
/// form's model into hooks of the server's format and back, with its messages for incomplete hooks.</summary>
[TestClass]
public class EventsEditorHooksTests
{
    private static string? Node() => new[] { "node", "node.exe" }.FirstOrDefault(n =>
    {
        try { using var p = Process.Start(new ProcessStartInfo(n, "--version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true }); p!.WaitForExit(5000); return p.ExitCode == 0; }
        catch (Exception) { return false; }
    });

    private static string Run(string node, string code)
    {
        var path = Path.Combine(Path.GetTempPath(), "sacred-hk-" + Guid.NewGuid().ToString("N") + ".js");
        File.WriteAllText(path, code);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(node, $"\"{path}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
            var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd();
            process.WaitForExit(10000);
            Assert.AreEqual(0, process.ExitCode, error);
            return output;
        }
        finally { File.Delete(path); }
    }

    private static string Fragment()
    {
        var script = EventsPageAssets.Script;
        var from = script.IndexOf("/* ---- hooks:", StringComparison.Ordinal);
        var to = script.IndexOf("$('#reload')", StringComparison.Ordinal);
        Assert.IsTrue(from > 0 && to > from, "the hooks code is in the page's script");
        return script[from..to];
    }

    [TestMethod]
    public void ThePageCarriesTheHooksEditorWithoutOutsideResources()
    {
        var script = EventsPageAssets.Script;
        foreach (var needle in new[] { "Hooks: what happens on the way", "hooksToDef", "readHooks", "wireHooks", "data-hk=\"aadd\"", "Progress reaches", "Time passed (minutes)", "Creatures of one kind defeated", "Spawn creatures", "Drop an item", "Drop gold", "Send a message", "the area\\'s owner", "that many for each hero" })
            StringAssert.Contains(script, needle);
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(script + EventsPageAssets.Css, @"https?://|@import"), "works offline");
        StringAssert.Contains(EventsPageAssets.Css, "#editor .hk");
    }

    [TestMethod]
    public void TheEditorsModelBecomesHooksOfTheServersFormatAndBack()
    {
        var node = Node();
        if (node is null) { Assert.Inconclusive("node is not installed"); return; }
        var code = "function esc(t){return String(t==null?'':t)}function num(v){var n=parseFloat(v);return isNaN(n)?undefined:n}\n" + Fragment() + """
            var out = {};
            var good = [
              { trig: 'at', v: '30', actions: [{ kind: 'spawn', target: 'owner', n: '12', radius: '600', per: true, text: 'Orcs!', kinds: [{ type: 'Orc Warrior', weight: '3' }, { type: ' ', weight: '1' }, { type: '38', weight: '' }] }] },
              { trig: 'at_time', v: '20', actions: [{ kind: 'message', target: 'global', text: 'Hurry' }] },
              { trig: 'kills', creature: 'Orc Shaman', count: '5', per: true, region: '2', actions: [{ kind: 'item', target: 'killer', n: '2', type: '', quality: '9', category: 'Ring', level: '' }, { kind: 'gold', target: 'killer', amount: '500' }] },
              { trig: 'success', actions: [{ kind: 'gold', target: 'players', amount: '100' }] },
            ];
            out.good = hooksToDef(good);
            out.back = out.good.map(hookToModel);
            out.again = hooksToDef(out.back);
            out.bad = [
              hooksToDef([{ trig: 'at', v: '0', actions: [] }]),
              hooksToDef([{ trig: 'at', v: '101', actions: [] }]),
              hooksToDef([{ trig: 'at_time', v: '', actions: [] }]),
              hooksToDef([{ trig: 'kills', creature: '', count: '3', actions: [] }]),
              hooksToDef([{ trig: 'start', actions: [] }]),
              hooksToDef([{ trig: 'start', actions: [{ kind: 'spawn', n: '0', kinds: [] }] }]),
              hooksToDef([{ trig: 'start', actions: [{ kind: 'spawn', n: '3', radius: '', kinds: [{ type: '', weight: '1' }] }] }]),
              hooksToDef([{ trig: 'start', actions: [{ kind: 'item', n: '1', type: '', quality: '', category: '', level: '' }] }]),
              hooksToDef([{ trig: 'start', actions: [{ kind: 'gold', amount: '' }] }]),
              hooksToDef([{ trig: 'start', actions: [{ kind: 'message', text: '  ' }] }]),
            ];
            out.clamp = hooksToDef([{ trig: 'start', actions: [{ kind: 'spawn', target: 'host', n: '9999', radius: '5', per: false, kinds: [{ type: 'a', weight: '99999' }] }] }]);
            console.log(JSON.stringify(out));
            """;
        var json = JsonDocument.Parse(Run(node, code)).RootElement;
        var good = json.GetProperty("good");
        Assert.AreEqual(4, good.GetArrayLength());
        Assert.AreEqual(30, good[0].GetProperty("at").GetDouble());
        var spawn = good[0].GetProperty("actions")[0];
        Assert.AreEqual("spawn", spawn.GetProperty("action").GetString());
        Assert.AreEqual("owner", spawn.GetProperty("target").GetString());
        Assert.AreEqual(12, spawn.GetProperty("n").GetInt32());
        Assert.AreEqual(600, spawn.GetProperty("radius").GetInt32());
        Assert.IsTrue(spawn.GetProperty("per_player").GetBoolean());
        Assert.AreEqual("Orcs!", spawn.GetProperty("text").GetString());
        var kinds = spawn.GetProperty("kinds");
        Assert.AreEqual(2, kinds.GetArrayLength(), "the empty kind is left out");
        Assert.AreEqual(3, kinds[0].GetProperty("weight").GetInt32());
        Assert.AreEqual(38, kinds[1].GetProperty("type").GetInt32(), "a number stays a number");
        Assert.AreEqual(1, kinds[1].GetProperty("weight").GetInt32(), "empty weight is 1");
        Assert.AreEqual(1200, good[1].GetProperty("at_time").GetInt32(), "minutes become seconds");
        var kills = good[2].GetProperty("kills");
        Assert.AreEqual("Orc Shaman", kills.GetProperty("creature").GetString());
        Assert.AreEqual(5, kills.GetProperty("count").GetInt32());
        Assert.IsTrue(kills.GetProperty("per_player").GetBoolean());
        Assert.AreEqual(2, kills.GetProperty("region").GetInt32(), "a kills trigger can be limited to a game region");
        var item = good[2].GetProperty("actions")[0];
        Assert.AreEqual("ring", item.GetProperty("spec").GetProperty("category").GetString(), "lower case");
        Assert.AreEqual(9, item.GetProperty("spec").GetProperty("quality").GetInt32());
        Assert.AreEqual(2, item.GetProperty("n").GetInt32());
        Assert.AreEqual(500, good[2].GetProperty("actions")[1].GetProperty("amount").GetInt32());
        Assert.AreEqual("success", good[3].GetProperty("on").GetString());
        Assert.AreEqual(good.GetRawText(), json.GetProperty("again").GetRawText(), "the server's hooks come back into the editor and out again unchanged");
        var bad = json.GetProperty("bad").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.IsTrue(bad.All(b => b is not null && b.StartsWith("Hook 1: ")), string.Join(" | ", bad));
        StringAssert.Contains(bad[0], "percent");
        StringAssert.Contains(bad[2], "minutes");
        StringAssert.Contains(bad[3], "creature");
        StringAssert.Contains(bad[4], "at least one action");
        StringAssert.Contains(bad[5], "how many");
        StringAssert.Contains(bad[6], "kind");
        StringAssert.Contains(bad[7], "item");
        StringAssert.Contains(bad[8], "gold");
        StringAssert.Contains(bad[9], "message");
        var clamp = json.GetProperty("clamp")[0].GetProperty("actions")[0];
        Assert.AreEqual(40, clamp.GetProperty("n").GetInt32());
        Assert.AreEqual(50, clamp.GetProperty("radius").GetInt32());
        Assert.IsFalse(clamp.GetProperty("per_player").GetBoolean());
        Assert.AreEqual(99999, clamp.GetProperty("kinds")[0].GetProperty("weight").GetInt32(), "sent as typed; the server accepts weights of 1 to 1000");
    }
}
