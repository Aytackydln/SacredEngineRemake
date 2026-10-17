using System;
using System.IO;
using System.Threading.Tasks;
using Sacred.Network.Admission;

namespace Sacred.Engine.Multiplayer;

/// <summary>Checks installation compatibility off the game thread and logs completion on the game thread.</summary>
internal sealed class GameServerCompatibilityCheck
{
    private Task<GoldLanCompatibility>? _pending;
    private string _status = "LAN files not checked; use set server-files check";

    public string Status => _status;
    public GoldLanCompatibility? Compatibility { get; private set; }

    public string Start(string gameDirectory)
    {
        if (_pending is not null) return _status;
        Compatibility = null;
        _status = "reading LAN build and file fingerprints";
        EngineLog.WriteLine($"Network debug: {_status}.");
        _pending = Task.Run(() => GoldLanCompatibility.Read(gameDirectory));
        return _status;
    }

    public void Update()
    {
        if (_pending is not { IsCompleted: true } task) return;
        _pending = null;
        try
        {
            var compatibility = task.GetAwaiter().GetResult();
            Compatibility = compatibility;
            _status = $"LAN file check complete; version {compatibility.Version}; 25 fingerprint fields; saved-character admission available; hero/world entry pending";
            EngineLog.WriteLine($"Network: {_status}.");
            for (var index = 0; index < GoldDataFingerprints.Count; index++)
                EngineLog.WriteLine($"Network fingerprint bit {index}: {GoldDataFingerprints.Fields[index].RelativePath}, 0x{compatibility.Fingerprints[index]:X8}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or OverflowException or BadImageFormatException)
        {
            _status = $"LAN file check failed: {error.Message}";
            EngineLog.WriteLine($"Network: {_status}.");
        }
    }
}
