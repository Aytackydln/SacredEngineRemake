using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Sacred.Engine.Cheats;

/// <summary>Reads live debug commands from standard input without blocking the render loop.</summary>
internal sealed class CheatsController : IDisposable
{
    private static readonly IReadOnlyDictionary<string, Func<string[], CheatCommand>> CommandParsers =
        new Dictionary<string, Func<string[], CheatCommand>>(StringComparer.OrdinalIgnoreCase)
        {
            ["help"] = static _ => new HelpCheatCommand(),
            ["teleport"] = ParseTeleport,
            ["tp"] = ParseTeleport,
            ["screenshot"] = ParseScreenshot,
            ["shot"] = ParseScreenshot,
            ["set"] = ParseSetOption,
            ["option"] = ParseSetOption,
            ["change"] = ParseSetOption
        };

    private readonly ConcurrentQueue<CheatCommand> _pendingCommands = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _readerTask;

    public CheatsController(TextReader input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _readerTask = Task.Run(() => ReadCommandsAsync(input, _shutdown.Token));
        EngineLog.WriteLine("Cheats ready. Type 'help' for commands.");
    }

    public void Update(Action<CheatCommand> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        while (_pendingCommands.TryDequeue(out var command))
            execute(command);
    }

    public void Dispose() => _shutdown.Cancel();

    private async Task ReadCommandsAsync(TextReader input, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                    return;

                if (!string.IsNullOrWhiteSpace(line))
                    _pendingCommands.Enqueue(Parse(line));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _pendingCommands.Enqueue(new InvalidCheatCommand($"Unable to read cheat input: {exception.Message}"));
        }
    }

    private static CheatCommand Parse(string line)
    {
        var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0
            ? new InvalidCheatCommand("Enter 'help' to list cheat commands.")
            : CommandParsers.TryGetValue(parts[0], out var parseCommand)
                ? parseCommand(parts)
                : new InvalidCheatCommand($"Unknown cheat command '{parts[0]}'. Type 'help' for commands.");
    }

    private static CheatCommand ParseTeleport(string[] parts) =>
        TryParsePosition(parts, out var position)
            ? new TeleportCheatCommand(position)
            : new InvalidCheatCommand("Usage: teleport <x> <y>");

    private static CheatCommand ParseSetOption(string[] parts) =>
        parts.Length == 3
            ? new SetOptionCheatCommand(parts[1], parts[2])
            : new InvalidCheatCommand("Usage: set <option> <value>");

    private static CheatCommand ParseScreenshot(string[] parts) =>
        parts.Length <= 2
            ? new ScreenshotCheatCommand(parts.Length == 2 ? parts[1] : null)
            : new InvalidCheatCommand("Usage: screenshot [label]");

    private static bool TryParsePosition(string[] parts, out Vector2 position)
    {
        position = default;
        if (parts.Length != 3 ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !float.IsFinite(x) || !float.IsFinite(y))
        {
            return false;
        }

        position = new Vector2(x, y);
        return true;
    }
}

internal abstract record CheatCommand;

internal sealed record HelpCheatCommand : CheatCommand;

internal sealed record TeleportCheatCommand(Vector2 Position) : CheatCommand;

internal sealed record ScreenshotCheatCommand(string? Label) : CheatCommand;

internal sealed record SetOptionCheatCommand(string Option, string Value) : CheatCommand;

internal sealed record InvalidCheatCommand(string Message) : CheatCommand;
