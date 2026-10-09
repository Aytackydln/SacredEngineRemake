using System;
using System.Collections.Generic;
using System.Linq;
using Sacred.Core.GameRes;

namespace Sacred.UI.Menus;

/// <summary>Modal selection and activation, independent of the renderer and platform input.</summary>
public sealed class EscapeMenuState(GameResStore resources)
{
    private bool _hasExport = true;
    private EscapeMenuEntry[] _entries = CreateEntries(resources, true);
    public bool IsOpen { get; private set; }
    public bool ConfirmingQuit { get; private set; }
    public string QuitPrompt => resources.GetString("UI_BUSY_2MAINMENU", "Do you want to quit the game?");
    public int SelectedIndex { get; private set; } = -1;
    public int? PressedIndex { get; private set; }
    public EscapeMenuLayout? Layout { get; private set; }
    public IReadOnlyList<EscapeMenuEntry> Entries => _entries;
    public event Action? Changed;
    public event Action? QuitRequested;

    public void SetLayout(EscapeMenuLayout layout)
    {
        Layout = layout;
        if (_hasExport == layout.HasExport) return;
        _hasExport = layout.HasExport;
        if (!ConfirmingQuit) ResetEntries();
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        ConfirmingQuit = false;
        ResetEntries();
        Changed?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        ConfirmingQuit = false;
        ResetEntries();
        Changed?.Invoke();
    }

    public void Escape()
    {
        if (!IsOpen) Open();
        else if (ConfirmingQuit) CancelQuit();
        else Close();
    }

    public void MoveSelection(int direction)
    {
        if (!IsOpen || direction == 0) return;
        var index = SelectedIndex;
        for (var n = 0; n < _entries.Length; n++)
        {
            index = (index + Math.Sign(direction) + _entries.Length) % _entries.Length;
            if (!_entries[index].Enabled) continue;
            SelectedIndex = index;
            return;
        }
    }

    public void Select(int? index)
    {
        if (IsOpen && index is { } i && i >= 0 && i < _entries.Length && _entries[i].Enabled)
            SelectedIndex = i;
    }

    public void Press(int? index)
    {
        CancelPointer();
        if (!IsOpen || index is not { } i || i < 0 || i >= _entries.Length || !_entries[i].Enabled) return;
        Select(i);
        PressedIndex = i;
    }

    public void Release(int? index)
    {
        var pressed = PressedIndex;
        CancelPointer();
        if (pressed.HasValue && pressed == index) Activate(_entries[pressed.Value].Action);
    }

    public void CancelPointer() => PressedIndex = null;
    public void ActivateSelected()
    {
        if (SelectedIndex >= 0) Activate(_entries[SelectedIndex].Action);
    }

    public bool Activate(EscapeMenuAction action)
    {
        if (!IsOpen || !_entries.Any(e => e.Action == action && e.Enabled)) return false;
        CancelPointer();
        switch (action)
        {
            case EscapeMenuAction.Continue: Close(); break;
            case EscapeMenuAction.Quit:
                ConfirmingQuit = true;
                _entries = [new(EscapeMenuAction.ConfirmQuit, resources.GetString("1034", "Yes"), true),
                    new(EscapeMenuAction.CancelQuit, resources.GetString("1035", "No"), true)];
                SelectedIndex = 1;
                Changed?.Invoke();
                break;
            case EscapeMenuAction.CancelQuit: CancelQuit(); break;
            case EscapeMenuAction.ConfirmQuit: QuitRequested?.Invoke(); break;
        }
        return true;
    }

    private void CancelQuit()
    {
        ConfirmingQuit = false;
        ResetEntries();
        Changed?.Invoke();
    }

    private void ResetEntries()
    {
        _entries = CreateEntries(resources, _hasExport);
        SelectedIndex = _entries.Length - 1;
        CancelPointer();
    }

    private static EscapeMenuEntry[] CreateEntries(GameResStore resources, bool export)
    {
        // Resource numbers and ordering are cUI_EscMenu::init1024's native text definitions.
        List<EscapeMenuEntry> entries = [new(EscapeMenuAction.Options, resources.GetString("1050", "Options"), false),
            new(EscapeMenuAction.Save, resources.GetString("1051", "Save Game"), false)];
        if (export) entries.Add(new(EscapeMenuAction.Export, resources.GetString("1049", "Export"), false));
        entries.Add(new(EscapeMenuAction.Quit, resources.GetString("1052", "Quit"), true));
        entries.Add(new(EscapeMenuAction.Continue, resources.GetString("1053", "Continue"), true));
        return entries.ToArray();
    }
}
