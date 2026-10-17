using System;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.Scripting;

namespace Sacred.Engine;

internal sealed partial class SacredGameRuntime
{
    private Task<WorldScriptExecutionContext>? _startupInspection;
    private CancellationTokenSource? _startupProbeCancellation;
    private bool TrySetServerStartupOption(string option, string value, out string message)
    {
        message = string.Empty;
        if (!option.Equals("server-startup", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            _startupProbeCancellation?.Cancel();
            message = "host startup probe cancellation requested";
            return true;
        }
        if (value.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            message = _startupInspection switch
            {
                null => "no authored startup inspection requested",
                { IsCompleted: false } => "authored startup inspection running",
                { IsCanceled: true } => "authored startup probe cancelled",
                { IsCompletedSuccessfully: true } task => task.Result.Failure?.Message ?? $"startup complete {task.Result.StartupCompleted}",
                _ => "authored startup inspection failed; see console"
            };
            return true;
        }
        if (_startupInspection is { IsCompleted: false }) { message = "wait for the current startup inspection"; return true; }
        var probe = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (probe.Length > 0 && probe[0].Equals("probe", StringComparison.OrdinalIgnoreCase))
        {
            if (probe.Length is < 2 or > 4 || !int.TryParse(probe[1], out var profile) || profile is < 1 or > 8 ||
                probe.Length == 4 && (!int.TryParse(probe[3], out var parsedPort) || parsedPort is < 1 or > 65535))
            { message = "server-startup probe <profile 1..8> [host] [port]; server must be empty"; return true; }
            var host = probe.Length >= 3 ? probe[2] : "127.0.0.1";
            var port = probe.Length == 4 ? int.Parse(probe[3]) : 2006;
            _startupProbeCancellation?.Dispose();
            _startupProbeCancellation = new CancellationTokenSource();
            var token = _startupProbeCancellation.Token;
            _startupInspection = Task.Run(async () => (await SacredNetworkHostStartupProbe.RunSavedCharacterAsync(_gameDirectory,
                SacredCampaignFiles.Resolve(_gameDirectory, _campaignSelection), profile, host, port, EngineLog.WriteLine, token)).Execution);
            _ = _startupInspection.ContinueWith(task => EngineLog.WriteLine($"Host startup probe failed: {task.Exception!.GetBaseException().Message}."), TaskContinuationOptions.OnlyOnFaulted);
            message = $"host startup probe started for {host}:{port}; use server-startup status or cancel";
            return true;
        }
        var selection = value.Equals("check", StringComparison.OrdinalIgnoreCase) ? _campaignSelection : value;
        try
        {
            var files = SacredCampaignFiles.Resolve(_gameDirectory, selection);
            _startupInspection = Task.Run(() => WorldScriptStartupInspection.RunFromInstallation(_gameDirectory, files, EngineLog.WriteLine));
            _ = _startupInspection.ContinueWith(task => EngineLog.WriteLine($"Network startup inspection failed: {task.Exception!.GetBaseException().Message}."),
                TaskContinuationOptions.OnlyOnFaulted);
            message = $"executing isolated authored startup for {files.Name}; use set server-startup status";
        }
        catch (Exception error) { message = error.Message; }
        return true;
    }
}
