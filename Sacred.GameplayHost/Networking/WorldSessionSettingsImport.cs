using Sacred.Core.Executable;

namespace Sacred.GameplayHost.Networking;

/// <summary>Complete Gold 0x7EDEA0 at explicit settings/string/platform service
/// boundaries. This operation does not establish actual getter/file bindings.</summary>
public static class WorldSessionSettingsImport
{
    public static void Apply(SacredSessionImportDefinition definition, WorldSessionImportServices services)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(services);
        var controller = services.Controller;
        var strings = services.Strings;
        services.RemoveValue(definition.SessionDataKey);
        services.RemoveValue(definition.CharacterKey);
        Key(WorldSessionSetting.CdKey, 0x39fc, "cdkey");
        Key(WorldSessionSetting.CdKey2, 0x3a08, "cdkey2");
        WorldNetworkAddressSelection.Apply(new(services.AddressManager,
            () => services.ReadString(WorldSessionSetting.Ip), services.ParseAddress, services.WriteAddressString,
            (stage, value) => services.Log(stage == "configured" ? "ip" : stage, Z(value), null)));
        strings.WriteAnsi(0x39e4, services.ReadString(WorldSessionSetting.Lobby).ToArray());
        services.Log("lobby", Z(strings.ReadAnsi(0x39e4)), null);
        Wide(WorldSessionSetting.Login, 0x3a14, "login");
        var lobbyPassword = services.DecodeAnsi(services.ReadString(WorldSessionSetting.LobbyPassword).ToArray());
        if (lobbyPassword is { Length: > 0 })
        {
            strings.WriteWide(0x39f0, lobbyPassword.Value.ToArray());
            services.Log("lobbypwd", default, null);
        }
        var lobbyPort = services.ReadInteger(WorldSessionSetting.LobbyPort);
        controller.WriteWord(0x3c, lobbyPort);
        services.Log("lobbyport", default, lobbyPort);
        Wide(WorldSessionSetting.Password, 0x3840, "password");
        Wide(WorldSessionSetting.Player, 0x3834, "player");
        var port = services.ReadInteger(WorldSessionSetting.ListenPort);
        controller.WriteWord(0x40, port);
        services.Log("port", default, port);
        Wide(WorldSessionSetting.Session, 0x3828, "session");
        if (services.ReadCurrentConnection() is not null)
        {
            var speed = services.ReadInteger(WorldSessionSetting.Speed);
            var connection = Current();
            connection.WriteWord(0x40414, speed);
            if (unchecked((int)speed) >= 3) connection.WriteWord(0x40414, 0);
            services.Log("speed", default, connection.ReadWord(0x40414));
        }
        if (services.ReadAseFlag() == 0 || services.ReadCurrentConnection() is null) return;
        var address = services.ReadString(WorldSessionSetting.AseAddress).ToArray();
        // This logger receives the complete by-value ANSI string, not a C string.
        services.Log("ase-ip", address, null);
        var parsed = services.ParseAddress(address);
        Current().WriteWord(0x40460, parsed);
        var asePort = services.ReadInteger(WorldSessionSetting.AsePort);
        Current().WriteWord(0x40464, asePort);
        services.Log("ase-port", default, asePort);
        var current = Current();
        if (current.ReadWord(0x40460) == 0 || current.ReadWord(0x40464) == 0)
        {
            current.WriteWord(0x40464, 0);
            current.WriteWord(0x40460, 0);
            services.WriteAseFlag(0);
            services.Log("invalid-ase", default, null);
        }

        WorldControlMemory Current() => services.ReadCurrentConnection()
            ?? throw new InvalidOperationException("Native session importer dereferenced a missing current connection.");
        void Key(WorldSessionSetting setting, uint offset, string stage)
        {
            strings.WriteAnsi(offset, services.ReadString(setting).ToArray());
            var value = strings.ReadAnsi(offset).ToArray();
            if (value.Length != 0)
            {
                if (value.Length < 5) throw new ArgumentOutOfRangeException(nameof(services), "Native CD-key masking requires at least five bytes.");
                value = value.AsSpan(0, 5).ToArray().Concat(value.Skip(Math.Min(value.Length, 25)))
                    .Concat(definition.MaskedSuffix.ToArray()).ToArray();
            }
            services.Log(stage, Z(value), null);
        }
        void Wide(WorldSessionSetting setting, uint offset, string stage)
        {
            var decoded = services.DecodeAnsi(services.ReadString(setting).ToArray());
            if (decoded is { } value) strings.WriteWide(offset, value.ToArray());
            services.Log(stage, Z(services.EncodeAnsi(strings.ReadWide(offset))), null);
        }
    }

    private static ReadOnlyMemory<byte> Z(ReadOnlyMemory<byte> value)
    {
        var end = value.Span.IndexOf((byte)0);
        return end < 0 ? value : value[..end];
    }
}
