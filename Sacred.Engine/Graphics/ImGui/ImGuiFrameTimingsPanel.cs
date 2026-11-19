using System;
using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Sacred.Engine.Diagnostics;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Frame-time graphs and the per-stage CPU timing table fed by <see cref="FrameProfiler"/>.
/// Statistics are recomputed a few times per second so the numbers stay readable and drawing does not allocate every frame.</summary>
internal static class ImGuiFrameTimingsPanel
{
    private const double RefreshIntervalMilliseconds = 250;
    private static readonly int StageCount = FrameStages.Count;
    private static readonly Vector4 WarnColour = new(1.00f, 0.75f, 0.25f, 1.0f);
    private static readonly Vector4 HotColour = new(1.00f, 0.40f, 0.35f, 1.0f);

    private static readonly float[] Scratch = new float[FrameProfiler.HistoryLength];
    private static readonly StageStatistics[] Stats = new StageStatistics[StageCount];
    private static readonly float[] Self = new float[StageCount];
    private static readonly bool[] HasChildren = new bool[StageCount];
    private static readonly string[] AverageText = new string[StageCount];
    private static readonly string[] MaximumText = new string[StageCount];
    private static readonly string[] P95Text = new string[StageCount];
    private static readonly string[] SelfText = new string[StageCount];
    private static readonly string[] ShareText = new string[StageCount];
    private static readonly float[] Share = new float[StageCount];
    private static string _intervalOverlay = string.Empty;
    private static string _activeOverlay = string.Empty;
    private static long _lastRefresh;
    private static bool _hideIdle = true;

    public static void Draw()
    {
        var enabled = FrameProfiler.Enabled;
        if (DearImGui.Checkbox("Collect frame timings", ref enabled))
        {
            FrameProfiler.Enabled = enabled;
            if (!enabled) FrameProfiler.Reset();
        }
        DearImGui.SameLine();
        if (DearImGui.Button("Reset history"))
        {
            FrameProfiler.Reset();
            _lastRefresh = 0;
        }
        if (!FrameProfiler.Enabled)
        {
            DearImGui.TextDisabled("Frame timing collection is off.");
            return;
        }
        if (FrameProfiler.FramesRecorded == 0)
        {
            DearImGui.TextDisabled("Waiting for frames...");
            return;
        }

        Refresh();
        DrawGraph("##frameInterval", FrameStage.FrameInterval, _intervalOverlay);
        DrawGraph("##activeFrame", FrameStage.Active, _activeOverlay);
        DearImGui.TextDisabled($"Last {FrameProfiler.FramesRecorded} frames. CPU time per region on the main thread; GPU execution is not included.");
        DearImGui.Checkbox("Hide idle stages", ref _hideIdle);
        DrawTable();
    }

    private static void DrawGraph(string id, FrameStage stage, string overlay) =>
        DearImGui.PlotLines(id, ref FrameProfiler.HistoryRow(stage), FrameProfiler.HistoryLength,
            FrameProfiler.HistoryOffset, overlay, 0.0f, float.MaxValue, new Vector2(0.0f, 44.0f));

    private static void DrawTable()
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH |
                                      ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit;
        if (!DearImGui.BeginTable("##frameTimings", 6, flags, new Vector2(0.0f, 380.0f)))
            return;
        DearImGui.TableSetupScrollFreeze(0, 1);
        DearImGui.TableSetupColumn("Stage", ImGuiTableColumnFlags.WidthStretch);
        DearImGui.TableSetupColumn("Avg ms", ImGuiTableColumnFlags.WidthFixed, 58.0f);
        DearImGui.TableSetupColumn("Max ms", ImGuiTableColumnFlags.WidthFixed, 58.0f);
        DearImGui.TableSetupColumn("P95 ms", ImGuiTableColumnFlags.WidthFixed, 58.0f);
        DearImGui.TableSetupColumn("Self ms", ImGuiTableColumnFlags.WidthFixed, 58.0f);
        DearImGui.TableSetupColumn("Share", ImGuiTableColumnFlags.WidthFixed, 70.0f);
        DearImGui.TableHeadersRow();

        for (var stage = 0; stage < StageCount; stage++)
        {
            if (stage == (int)FrameStage.FrameInterval)
                continue; // Shown as the graph above; every share is relative to it.
            var stats = Stats[stage];
            if (_hideIdle && stats.MaximumMilliseconds < 0.01f)
                continue;

            DearImGui.TableNextRow();
            DearImGui.TableSetColumnIndex(0);
            DearImGui.TextUnformatted(FrameStages.Label(stage));
            var colour = Share[stage] >= 0.25f ? HotColour : Share[stage] >= 0.10f ? WarnColour : default;
            var coloured = colour != default;
            if (coloured) DearImGui.PushStyleColor(ImGuiCol.Text, colour);
            DearImGui.TableSetColumnIndex(1);
            DearImGui.TextUnformatted(AverageText[stage]);
            DearImGui.TableSetColumnIndex(2);
            DearImGui.TextUnformatted(MaximumText[stage]);
            DearImGui.TableSetColumnIndex(3);
            DearImGui.TextUnformatted(P95Text[stage]);
            if (coloured) DearImGui.PopStyleColor();
            DearImGui.TableSetColumnIndex(4);
            if (HasChildren[stage]) DearImGui.TextDisabled(SelfText[stage]);
            DearImGui.TableSetColumnIndex(5);
            DearImGui.ProgressBar(Share[stage], new Vector2(-1.0f, 0.0f), ShareText[stage]);
        }
        DearImGui.EndTable();
    }

    private static void Refresh()
    {
        if (_lastRefresh != 0 && Stopwatch.GetElapsedTime(_lastRefresh).TotalMilliseconds < RefreshIntervalMilliseconds)
            return;
        _lastRefresh = Stopwatch.GetTimestamp();

        for (var stage = 0; stage < StageCount; stage++)
        {
            Stats[stage] = FrameProfiler.Statistics((FrameStage)stage, Scratch);
            Self[stage] = Stats[stage].AverageMilliseconds;
            HasChildren[stage] = false;
        }
        // Self time = a stage's average minus the averages of its direct children.
        for (var stage = 0; stage < StageCount; stage++)
        {
            var parent = FrameStages.ParentOf(stage);
            if (parent < 0) continue;
            HasChildren[parent] = true;
            Self[parent] -= Stats[stage].AverageMilliseconds;
        }

        var interval = Math.Max(Stats[(int)FrameStage.FrameInterval].AverageMilliseconds, 0.0001f);
        for (var stage = 0; stage < StageCount; stage++)
        {
            var stats = Stats[stage];
            Share[stage] = Math.Clamp(stats.AverageMilliseconds / interval, 0.0f, 1.0f);
            AverageText[stage] = $"{stats.AverageMilliseconds:0.00}";
            MaximumText[stage] = $"{stats.MaximumMilliseconds:0.00}";
            P95Text[stage] = $"{stats.P95Milliseconds:0.00}";
            SelfText[stage] = $"{Math.Max(0.0f, Self[stage]):0.00}";
            ShareText[stage] = $"{Share[stage] * 100.0f:0}%";
        }
        _intervalOverlay = Overlay("Frame interval", Stats[(int)FrameStage.FrameInterval]);
        _activeOverlay = Overlay("Active (CPU, incl. present)", Stats[(int)FrameStage.Active]);
    }

    private static string Overlay(string name, StageStatistics stats) =>
        $"{name}: avg {stats.AverageMilliseconds:0.00} ms ({(stats.AverageMilliseconds > 0 ? 1000.0f / stats.AverageMilliseconds : 0):0} FPS), max {stats.MaximumMilliseconds:0.00} ms";
}
