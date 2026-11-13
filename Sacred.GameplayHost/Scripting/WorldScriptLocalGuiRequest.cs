namespace Sacred.GameplayHost.Scripting;

/// <summary>Owned five-word local event submitted to Gold's event manager.
/// Retained startup requests require a presentation consumer before UI acceptance;
/// they are independent of network publication and preserve submission order.</summary>
public readonly record struct WorldScriptLocalGuiRequest(uint Kind, uint Word2, uint Word3, uint Word4, uint Word5);
