using System.Text.Json;
using System.Text.Json.Serialization;
using Sacred.Granny.Diagnostics;

namespace AssetViewer.Browser.Rendering;

internal static class BrowserAnimationDiagnostics
{
    internal static string Execute(string argument)
    {
        if (argument is "on" or "off") AnimationPerformance.Enabled = argument == "on";
        else if (argument is not ("" or "reset")) throw new ArgumentException("Usage: animation-perf [on|off|reset]");
        var report = new BrowserAnimationReport(Enum.GetNames<AnimationCpuStage>(), AnimationPerformance.CaptureAndReset());
        return JsonSerializer.Serialize(report, BrowserAnimationJsonContext.Default.BrowserAnimationReport);
    }
}

internal sealed record BrowserAnimationReport(string[] Stages, AnimationPerformanceSnapshot Snapshot);
[JsonSerializable(typeof(BrowserAnimationReport))]
internal partial class BrowserAnimationJsonContext : JsonSerializerContext;
