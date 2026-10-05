using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using Sacred.Core;
using Sacred.Core.GameBin.Scripts;
using Sacred.Engine.Assets;
using Sacred.Engine.Scene;

namespace Sacred.Engine;

internal sealed partial class SacredGameRuntime
{
    private SacredGameDirectories _campaignDirectories = null!;
    private SacredGameSaveState _campaignLoadState = null!;
    private string _campaignSelection = SacredCampaignFiles.DefaultDirectoryName;
    private bool _campaignReloadPending;
    private readonly ConcurrentQueue<string> _campaignRequests = new();

    internal void RequestCampaignChange(string selection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(selection);
        _campaignRequests.Enqueue(selection);
    }

    private void ApplyCampaignRequests()
    {
        if (_debugUiControls.RequestedCampaignListRefresh)
        {
            _debugUiControls.RequestedCampaignListRefresh = false;
            _debugUiControls.Campaigns = SacredCampaignFiles.Discover(_gameDirectory);
            EngineLog.WriteLine($"Debug input: refreshed {_debugUiControls.Campaigns.Count} campaign choices.");
        }
        if (_debugUiControls.RequestedCampaign is { } requested)
        {
            _debugUiControls.RequestedCampaign = null;
            _campaignRequests.Enqueue(requested);
            EngineLog.WriteLine($"Debug input: campaign {requested}.");
        }
        while (_campaignRequests.TryDequeue(out var selection))
        {
            TrySetCampaign(selection, out var message);
            EngineLog.WriteLine($"Campaign request: {message}");
        }
    }

    private void InitializeCampaignSelection(SacredGameDirectories directories, SacredGameSaveState state)
    {
        _campaignDirectories = directories;
        _campaignLoadState = state;
        _campaignSelection = directories.CampaignScriptsDirectory ?? state.CampaignScriptsDirectory
            ?? SacredCampaignFiles.DefaultDirectoryName;
        _debugUiControls.Campaigns = SacredCampaignFiles.Discover(_gameDirectory);
    }

    private void SynchronizeCampaignControls()
    {
        var files = _inGameScene?.CampaignScripts?.Files;
        _debugUiControls.CampaignDirectoryPath = files?.DirectoryPath;
        _debugUiControls.CampaignDisplayName = files?.Name ?? _campaignSelection;
        _debugUiControls.CampaignChangeAvailable = _inGameScene is not null && !_campaignReloadPending &&
            _scenes.ActiveSceneId != GameSceneId.GameLoading;
    }

    private GameLoadingScene CreateGameLoadingScene()
    {
        var steps = _campaignReloadPending
            ? _resourceLoader.CreateInitialLoadSteps().Concat(_resourceLoader.CreateGameLoadSteps()).ToArray()
            : _resourceLoader.CreateGameLoadSteps();
        return new GameLoadingScene(steps, _resourceLoader.PakDirectory, _gameDirectory,
            InitializeRuntime, _scenes.RequestSwitch);
    }

    private bool TrySetCampaign(string value, out string message)
    {
        if (value.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var files in SacredCampaignFiles.Discover(_gameDirectory))
                EngineLog.WriteLine($"Campaign available: {files.Name} ({files.DirectoryPath}).");
            message = $"selected campaign: {_campaignSelection}";
            return true;
        }
        if (value.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            var scripts = _inGameScene?.CampaignScripts;
            message = scripts is null ? $"campaign {_campaignSelection}: loading" :
                $"campaign {scripts.Files.Name}: {scripts.Functions.Count:N0} functions, " +
                $"{scripts.Positions.Count:N0} positions, quest code {scripts.QuestCode.Length} bytes, " +
                $"quest pool code {scripts.QuestPoolCode.Length} bytes";
            if (_campaignReloadPending) message += $"; reloading {_campaignSelection}";
            return true;
        }
        if (_inGameScene is null || _campaignReloadPending || _scenes.ActiveSceneId == GameSceneId.GameLoading)
        {
            message = "Wait for the world to finish loading before changing campaign.";
            return false;
        }
        SacredCampaignFiles filesToLoad;
        try { filesToLoad = SacredCampaignFiles.Resolve(_gameDirectory, value); }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
        {
            message = $"Campaign change rejected: {error.Message}";
            return false;
        }

        // Reuse the established loading/preparation pipeline. Replacing the whole scene clears
        // sector objects, stairs guards, portal guards, particles and future quest runtime state.
        _campaignLoadState = CaptureSaveState() with { CampaignScriptsDirectory = filesToLoad.DirectoryPath };
        _campaignSelection = filesToLoad.DirectoryPath;
        _pendingInspection = null;
        _resourceLoader.Dispose();
        _resourceLoader = new GameResourceLoader(_campaignDirectories, _grannyBackend)
        {
            CampaignScriptsDirectory = _campaignSelection
        };
        _campaignReloadPending = true;
        _scenes.RequestSwitch(GameSceneId.GameLoading);
        message = $"reloading campaign {filesToLoad.Name}; preserving character and world position";
        return true;
    }
}
