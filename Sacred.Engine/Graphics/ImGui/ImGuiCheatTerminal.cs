using System.Numerics;
using ImGuiNET;
using Sacred.Engine.Cheats;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Displays the engine transcript above a separate editable cheat prompt.</summary>
internal sealed unsafe class ImGuiCheatTerminal
{
    private string _command = "";
    private long _outputVersion = -1;
    private bool _scrollToBottom;

    public void Build(CheatTerminalState terminal, ImFontPtr font)
    {
        if (!terminal.IsOpen)
            return;

        var displaySize = DearImGui.GetIO().DisplaySize;
        DearImGui.SetNextWindowSize(Vector2.Min(new Vector2(850, 430), displaySize * 0.9f), ImGuiCond.FirstUseEver);
        DearImGui.SetNextWindowPos(displaySize * 0.05f, ImGuiCond.FirstUseEver);
        if (terminal.FocusInputRequested)
            DearImGui.SetNextWindowFocus();
        var open = terminal.IsOpen;
        var drawContents = DearImGui.Begin("Cheat terminal (F11)", ref open);
        terminal.SetOpen(open);
        if (drawContents)
        {
            DearImGui.PushFont(font);
            DrawContents(terminal);
            DearImGui.PopFont();
        }
        DearImGui.End();
    }

    private void DrawContents(CheatTerminalState terminal)
    {
        DearImGui.TextDisabled("Enter runs a command. Type help for commands. F11 / Escape closes.");
        DearImGui.SameLine();
        if (DearImGui.SmallButton("Clear output"))
            TerminalOutput.Clear();
        DearImGui.Separator();

        var output = TerminalOutput.Snapshot();
        var footerHeight = DearImGui.GetFrameHeightWithSpacing() + DearImGui.GetStyle().ItemSpacing.Y;
        if (DearImGui.BeginChild("##terminal-output", new Vector2(0, -footerHeight), ImGuiChildFlags.Borders,
                ImGuiWindowFlags.HorizontalScrollbar))
        {
            var atBottom = DearImGui.GetScrollY() >= DearImGui.GetScrollMaxY();
            var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
            clipper.Begin(output.Lines.Length);
            while (clipper.Step())
                for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
                    DearImGui.TextUnformatted(output.Lines[index]);
            clipper.Destroy();
            if (_scrollToBottom || (atBottom && output.Version != _outputVersion))
                DearImGui.SetScrollHereY(1.0f);
            _scrollToBottom = false;
            _outputVersion = output.Version;
        }
        DearImGui.EndChild();
        DearImGui.Separator();

        DearImGui.SetNextItemWidth(-1);
        if (terminal.FocusInputRequested)
        {
            DearImGui.SetKeyboardFocusHere();
            terminal.FocusInputRequested = false;
        }
        if (DearImGui.InputTextWithHint("##terminal-command", "Enter a cheat command...", ref _command, 4096,
                ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (!string.IsNullOrWhiteSpace(_command))
            {
                terminal.Commands.Enqueue(_command);
                _command = "";
                _scrollToBottom = true;
            }
            DearImGui.SetKeyboardFocusHere(-1);
        }
    }
}
