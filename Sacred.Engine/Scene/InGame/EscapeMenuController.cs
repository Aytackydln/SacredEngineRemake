using System;
using System.Numerics;
using Sacred.Engine.Platform;
using Sacred.UI.Menus;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Owns input while the Escape menu is open, including the closing frame.</summary>
internal sealed class EscapeMenuController
{
    private readonly EscapeMenuState _menu;
    private readonly InputState _input;
    private readonly Func<Vector2> _viewport;
    private Vector2 _lastPointer;
    private bool _changed;

    public EscapeMenuController(EscapeMenuState menu, InputState input, Func<Vector2> viewport,
        Action suspendGameplay, Action quit)
    {
        _menu = menu;
        _input = input;
        _viewport = viewport;
        menu.QuitRequested += quit;
        menu.Changed += () =>
        {
            _changed = true;
            _lastPointer = input.MousePosition;
            suspendGameplay();
            input.ClearTransientEvents();
            input.MenuWantsInput = menu.IsOpen;
            EngineLog.WriteLine($"Escape menu: {(menu.IsOpen ? menu.ConfirmingQuit ? "quit confirmation" : "open; gameplay paused" : "closed; gameplay resumed")}.");
        };
    }

    public bool Update()
    {
        var wasOpen = _menu.IsOpen;
        if (_input.ConsumePressed(VirtualKey.Escape)) _menu.Escape();
        if (_menu.IsOpen)
        {
            if (_input.ConsumePressed(VirtualKey.Up)) { _menu.MoveSelection(-1); LogInput("Up"); }
            if (_input.ConsumePressed(VirtualKey.Down)) { _menu.MoveSelection(1); LogInput("Down"); }
            if (_input.ConsumePressed(VirtualKey.Enter)) { LogInput("Enter"); _menu.ActivateSelected(); }
            var viewport = _viewport();
            int? Hit(Vector2 point) => _menu.Layout is null ? null :
                EscapeMenuLayout.HitTest(point, viewport, _menu.Entries.Count, _menu.ConfirmingQuit);
            if (_input.MousePosition != _lastPointer)
            {
                _menu.Select(Hit(_input.MousePosition));
                _lastPointer = _input.MousePosition;
            }
            if (_input.TryConsumeLeftClick(out var position)) { _menu.Press(Hit(position)); LogInput("pointer down"); }
            if (_input.ConsumeLeftMouseButtonReleased()) { LogInput("pointer up"); _menu.Release(Hit(_input.MousePosition)); }
            else if (!_input.IsLeftMouseButtonDown) _menu.CancelPointer();
        }
        var consumed = wasOpen || _menu.IsOpen || _changed;
        _changed = false;
        if (consumed) _input.ClearTransientEvents();
        _input.MenuWantsInput = _menu.IsOpen;
        return consumed;
    }

    private void LogInput(string gesture) => EngineLog.WriteLine(
        $"Debug input: Escape menu {gesture}; selected {_menu.Entries[_menu.SelectedIndex].Action}.");

    public bool TrySetCheatOption(string option, string value, out string message)
    {
        message = string.Empty;
        if (!option.Equals("menu", StringComparison.OrdinalIgnoreCase)) return false;
        switch (value.ToLowerInvariant())
        {
            case "open": _menu.Open(); break;
            case "close": _menu.Close(); break;
            case "escape" or "toggle": _menu.Escape(); break;
            case "up": _menu.MoveSelection(-1); break;
            case "down": _menu.MoveSelection(1); break;
            case "activate": _menu.ActivateSelected(); break;
            case "status": break;
            default:
                if (!Enum.TryParse<EscapeMenuAction>(value, true, out var action) || !Enum.IsDefined(action))
                { message = "menu: open, close, escape, up, down, activate, status or an action name"; return true; }
                if (!_menu.Activate(action))
                { message = $"Escape menu action {action} unavailable"; return true; }
                break;
        }
        var selected = _menu.SelectedIndex >= 0 ? _menu.Entries[_menu.SelectedIndex].Action.ToString() : "none";
        message = $"Escape menu {(_menu.IsOpen ? _menu.ConfirmingQuit ? "confirming quit" : "open" : "closed")}; {(_menu.Layout is null ? "loading" : "ready")}; selected {selected}";
        return true;
    }
}
