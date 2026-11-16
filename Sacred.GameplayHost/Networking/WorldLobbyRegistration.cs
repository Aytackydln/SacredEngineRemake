using Sacred.Core.Executable;

namespace Sacred.GameplayHost.Networking;

/// <summary>Original lobby registration control flow. Per-value native statuses
/// are ignored by the caller; a create failure skips values/close. SetValue owns
/// translating any platform status to the original nonthrowing return boundary.</summary>
public static class WorldLobbyRegistration
{
    public static void Register(SacredLobbyRegistrationDefinition definition, WorldLobbyRegistrationServices services)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(services);
        var rawDirectory = services.ReadDirectory(definition.DirectoryCapacity).Span;
        var terminator = rawDirectory.IndexOf((byte)0);
        var directory = (terminator < 0 ? rawDirectory : rawDirectory[..terminator]).ToArray();
        var result = services.CreateKey(definition.Root, definition.KeyPath, definition.Access);
        if (result.Status != 0) return;
        foreach (var value in definition.Values)
        {
            var data = (value.Literal ?? directory).Span;
            var terminated = new byte[data.Length + 1];
            data.CopyTo(terminated);
            services.SetValue(result.Handle, value.Name, value.Type, terminated);
        }
        services.CloseKey(result.Handle);
    }
}
