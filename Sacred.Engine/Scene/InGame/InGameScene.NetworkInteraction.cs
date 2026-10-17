using System;

namespace Sacred.Engine.Scene.InGame;

internal sealed partial class InGameScene
{
    private bool TrySetNetworkInteraction(string option, string value, out string message)
    {
        message = string.Empty;
        if (!option.Equals("server-interact", StringComparison.OrdinalIgnoreCase)) return false;
        if (_networkMovement is null || !NetworkWorldReady)
        { message = "Network interactions wait for a bound participant world and model."; return true; }
        if (value.Equals("status", StringComparison.OrdinalIgnoreCase))
        { message = _networkMovement.InteractionStatus; return true; }
        if (!uint.TryParse(value, out var reference))
        { message = "Use set server-interact <runtime object reference|status>."; return true; }
        try
        {
            _networkMovement.Interact(reference);
            message = _networkMovement.InteractionStatus;
        }
        catch (Exception error) { message = "Network interaction: " + error.Message; }
        return true;
    }
}
