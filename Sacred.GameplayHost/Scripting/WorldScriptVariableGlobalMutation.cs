namespace Sacred.GameplayHost.Scripting;

public sealed record WorldScriptVariableGlobalMutation(string Name, int? Index, int Value, bool Increment);
