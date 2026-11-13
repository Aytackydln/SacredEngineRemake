namespace Sacred.GameplayHost.Scripting;

/// <summary>Explicit native 0x7DB230 inputs and the local-actor reference passed
/// at 0x470CB7. Reference zero represents a null actor, not an invented hero.</summary>
public sealed record WorldScriptPostStartupInputs(bool ConnectionPresent, bool Connected,
    uint GameFlags, uint LocalActorReference);

/// <summary>Caller selection, separate from function-body completion. Index zero
/// can be selected by name but the native function dispatcher does not execute it.</summary>
public sealed record WorldScriptPostStartupInvocation(int FunctionIndex, uint ActorReference);
