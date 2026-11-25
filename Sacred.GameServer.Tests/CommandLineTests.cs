using Sacred.GameServer.Config;

namespace Sacred.GameServer.Tests;

/// <summary>The command line is strict: unknown options and bad values are refused before anything is bound.</summary>
[TestClass]
public class CommandLineTests
{
    [TestMethod]
    public void AnUnknownOptionIsRefusedWithTheClosestName()
    {
        var error = CommandLine.Validate(["--tcp-port=2006"]);
        Assert.IsNotNull(error);
        StringAssert.Contains(error, "unknown option '--tcp-port'");
        StringAssert.Contains(error, "did you mean --network-port-tcp?");
        StringAssert.Contains(CommandLine.Validate(["--webui-prt=1"])!, "did you mean --webui-port?");
        StringAssert.Contains(CommandLine.Validate(["--lan-anounce=0"])!, "did you mean --lan-announce?");
        var far = CommandLine.Validate(["--zzzzzzzz"])!;
        StringAssert.Contains(far, "unknown option '--zzzzzzzz'");
        Assert.IsFalse(far.Contains("did you mean"));
    }

    [TestMethod]
    public void QuietAndHelpAreAccepted()
    {
        Assert.IsNull(CommandLine.Validate(["quiet"]));
        Assert.IsNull(CommandLine.Validate(["QUIET", "--udp-port=0"]));
        Assert.IsNull(CommandLine.Validate(["--help"]));
        Assert.IsNull(CommandLine.Validate(["-h"]));
        Assert.IsNull(CommandLine.Validate([]));
    }

    [TestMethod]
    public void BadValuesAreRefused()
    {
        StringAssert.Contains(CommandLine.Validate(["--network-port-tcp=abc"])!, "needs a number");
        StringAssert.Contains(CommandLine.Validate(["--network-port-tcp=70000"])!, "between 0 and 65535");
        StringAssert.Contains(CommandLine.Validate(["--udp-port"])!, "needs a value");
        StringAssert.Contains(CommandLine.Validate(["--tick-ms=1"])!, "between 5 and 100");
        StringAssert.Contains(CommandLine.Validate(["--lan-announce=maybe"])!, "takes 0 or 1");
        StringAssert.Contains(CommandLine.Validate(["--game-dir"])!, "needs a value");
        StringAssert.Contains(CommandLine.Validate(["12abc"])!, "unexpected argument '12abc'");
        StringAssert.Contains(CommandLine.Validate(["-5"])!, "unexpected argument '-5'");
        StringAssert.Contains(CommandLine.Validate(["99999999999"])!, "unexpected argument");
        Assert.IsNull(CommandLine.Validate(["--network-password="]));             // an empty password is allowed
    }

    [TestMethod]
    public void TheCommandLineSacredExeUsesIsAccepted()
    {
        // Sacred.exe: CreateProcess("GameServer.exe", "quiet <HWND>[ netlog][ tincatlog]"): the first word is the program name slot,
        // so the server's arguments are the decimal window handle and the optional log words.
        Assert.IsNull(CommandLine.Validate(["1313466"]));
        Assert.IsNull(CommandLine.Validate(["1313466", "netlog"]));
        Assert.IsNull(CommandLine.Validate(["1313466", "netlog", "tincatlog"]));
        Assert.IsNull(CommandLine.Validate(["quiet", "1313466", "tincatlog"]));        // started by hand with the full line
        Assert.IsNull(CommandLine.Validate(["quiet"]));
        Assert.AreEqual(1313466L, CommandLine.WindowHandle(["1313466"]));
        Assert.AreEqual(1313466L, CommandLine.WindowHandle(["quiet", "1313466"]));
        Assert.AreEqual(1313466L, CommandLine.WindowHandle(["1313466", "--webui-port", "18099"]));
        Assert.AreEqual(0L, CommandLine.WindowHandle(["--webui-port", "18099"]), "the value of a spaced option is no window handle");
        Assert.AreEqual(0L, CommandLine.WindowHandle(["quiet"]));
        Assert.AreEqual(0L, CommandLine.WindowHandle(["netlog", "1313466"]), "only args[0], or args[1] behind quiet");
        Assert.IsNull(CommandLine.Validate(["--webui-port", "18099"]));
        Assert.IsNull(CommandLine.Validate(["1313466", "--webui-port", "18099"]));
        StringAssert.Contains(CommandLine.Validate(["--bind=127.0.0.1", "1313466"])!, "unexpected argument '1313466'");   // a number later on
        StringAssert.Contains(CommandLine.Validate(["netlog", "1313466"])!, "unexpected argument '1313466'");
        StringAssert.Contains(CommandLine.Validate(["quiet", "--authoritative", "1313466"])!, "unexpected argument '1313466'");
        var config = GameServerConfig.Load(["1313466", "tincatlog"]);
        Assert.AreEqual(1313466L, config.ParentWindow);
        Assert.IsTrue(config.Trace);
        Assert.IsFalse(GameServerConfig.Load(["1313466"]).Trace);
        // unknown options stay refused, also behind the number
        StringAssert.Contains(CommandLine.Validate(["1313466", "--tcp-port=2006"])!, "unknown option");
    }

    [TestMethod]
    public void SpacedValuesAndTheColonSpellingOfTheWebUiWork()
    {
        Assert.IsNull(CommandLine.Validate(["--webserver", "--webui-port", "18080", "--webui-password", "x"]));
        Assert.IsNull(CommandLine.Validate(["--WebUi:Port=18080", "--WebUi:Hostname", "127.0.0.1", "--webui-config", "x.json"]));
        StringAssert.Contains(CommandLine.Validate(["--WebUi:Prot=1"])!, "unknown option");
        // without the web UI exemption a spaced value is an unexpected argument
        StringAssert.Contains(CommandLine.Validate(["--network-port-tcp", "26006"])!, "needs a value");
    }

    [TestMethod]
    public void EveryOptionInTheHelpParsesAndIsListed()
    {
        var help = CommandLine.HelpText();
        foreach (var spec in CommandLine.Options)
        {
            StringAssert.Contains(help, "--" + spec.Name);
            foreach (var name in spec.AllNames())
            {
                var arg = spec.Kind == OptionKind.Flag ? $"--{name}" : $"--{name}={spec.Sample}";
                Assert.IsNull(CommandLine.Validate([arg]), arg);
                Assert.IsNull(CommandLine.Validate([$"--{name.ToUpperInvariant().Replace('-', '_')}" + (spec.Kind == OptionKind.Flag ? "" : "=" + spec.Sample)]), name);
            }
        }
    }

    [TestMethod]
    public void TheOptionTableHasNoDuplicateNamesOrAliases()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var spec in CommandLine.Options)
            foreach (var name in spec.AllNames())
            {
                var key = name.Replace('_', '-').ToLowerInvariant();
                Assert.IsFalse(seen.TryGetValue(key, out var other), $"'{name}' of --{spec.Name} is also a name or alias of --{other}");
                seen[key] = spec.Name;
            }
        Assert.AreEqual(1, CommandLine.Options.Count(o => o.Name == "bind"), "one --bind");
        CollectionAssert.AreEquivalent(new[] { "bind-address", "network-bind" }, CommandLine.Options.Single(o => o.Name == "bind").Aliases!);
    }

    [TestMethod]
    public void EveryKeyTheConfigParserKnowsHasAnOption()
    {
        // The keys of the original's Gameserver.cfg plus the options of the later stages must all be accepted on the command line.
        foreach (var key in new[] { "NETWORK_INTERNET", "NETWORK_IP_ADDRESS", "NETWORK_LOBBY", "NETWORK_LOBBYPORT", "NETWORK_NOCONFIRMATION",
                     "NETWORK_PASSWORD", "NETWORK_PLAYERDATA", "NETWORK_PORT_LISTEN", "NETWORK_PORT_TCP", "NETWORK_PRECONFIG_LEVEL",
                     "NETWORK_PRECONFIG_MAXPLAYERS", "NETWORK_PRECONFIG_MODE", "NETWORK_SESSION", "LADDER_EXPORT", "AUTHORITATIVE",
                     "FLOOD_PROTECTION", "MAX_CONNECTIONS_PER_ADDRESS", "CLOSED_NET", "LOBBY_CHANNEL", "UDP_PORT", "TRACE",
                     "NETOPS", "NETOPS_CONTROL", "NETOPS_DATA", "NETOPS_MAX_FRAGMENT", "NETOPS_MAX_MESSAGE", "NETOPS_MAX_IN_FLIGHT", "NETOPS_RATE",
                     "NETOPS_BURST", "NETOPS_MAX_CHANNELS", "NETOPS_MAX_NAME", "NETOPS_REASSEMBLY_SESSION", "NETOPS_REASSEMBLY_TOTAL",
                     "NETOPS_FRAGMENT_TIMEOUT", "NETOPS_HANDLER_BUDGET" })
            Assert.IsNotNull(CommandLine.Find(key), key);
    }
}
