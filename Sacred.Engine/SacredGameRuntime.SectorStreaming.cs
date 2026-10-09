using Sacred.World;

namespace Sacred.Engine;

internal sealed partial class SacredGameRuntime
{
    private SectorLoadMode _sectorLoadMode = SectorLoadMode.Four;
    private bool _waitForSectorGpuUploads;

    private void ApplySectorStreamingRequests()
    {
        if (_debugUiControls.RequestedSectorLoadMode is { } mode)
        {
            _debugUiControls.RequestedSectorLoadMode = null;
            SetSectorLoadMode(mode);
        }
        if (_debugUiControls.RequestedWaitForSectorGpuUploads is { } wait)
        {
            _debugUiControls.RequestedWaitForSectorGpuUploads = null;
            SetWaitForSectorGpuUploads(wait);
        }
    }

    private void SetSectorLoadMode(SectorLoadMode mode)
    {
        _sectorLoadMode = mode;
        _inGameScene?.SetSectorLoadMode(mode);
        EngineLog.WriteLine($"Debug input: loaded sectors set to {(int)mode} sectors.");
    }

    private void SetWaitForSectorGpuUploads(bool wait)
    {
        _waitForSectorGpuUploads = wait;
        if (_renderer.WorldInitialized) _renderer.WaitForSectorGpuUploads = wait;
        EngineLog.WriteLine($"Debug input: sector GPU upload waiting at load {(wait ? "enabled" : "disabled")}.");
    }

    private bool TrySetSectorStreamingOption(string option, string value, out string message)
    {
        switch (option.ToLowerInvariant())
        {
            case "sectors" when value == "4" || value == "9":
                SetSectorLoadMode(value == "4" ? SectorLoadMode.Four : SectorLoadMode.Nine);
                message = $"loaded sectors: {(int)_sectorLoadMode}";
                return true;
            case "sector-upload-wait" when TryParseBoolean(value, out var wait):
                SetWaitForSectorGpuUploads(wait);
                message = $"sector GPU upload waiting {(wait ? "enabled" : "disabled")}";
                return true;
        }
        message = string.Empty;
        return false;
    }
}
