using Sacred.Engine.Platform;

namespace Sacred.Engine;

internal sealed partial class SacredGameRuntime
{
    private void UpdateCheatTerminal()
    {
        var terminal = _debugUiControls.Terminal;
        var wasOpen = _window.Input.TerminalWantsInput;
        if (_window.Input.ConsumePressed(VirtualKey.F11))
            terminal.SetOpen(!terminal.IsOpen);
        if (terminal.IsOpen && _window.Input.ConsumePressed(VirtualKey.Escape))
            terminal.SetOpen(false);
        _window.Input.TerminalWantsInput = terminal.IsOpen;
        // Typed shortcut letters must not remain queued when the terminal closes.
        if (terminal.IsOpen || wasOpen)
            _window.Input.DiscardKeyboardPresses();
        while (terminal.Commands.TryDequeue(out var command))
            _cheats.Submit(command);
    }
}
