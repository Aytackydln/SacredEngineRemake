using System.Text.Json.Serialization;

namespace Sacred.GameServer.Web;

public sealed record CommandRequest(int Slot = 0, int Value = 0, string? Text = null, string? Reason = null, string? Address = null);
public sealed record CommandResult(bool Ok, string? Error);
public sealed record ChatPage(long Last, ChatEntry[] Entries);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ServerOverview))]
[JsonSerializable(typeof(PlayerInfo[]))]
[JsonSerializable(typeof(BanEntry[]))]
[JsonSerializable(typeof(ChatPage))]
[JsonSerializable(typeof(ChatEntry))]
[JsonSerializable(typeof(CommandRequest))]
[JsonSerializable(typeof(CommandResult))]
[JsonSerializable(typeof(FloodSettings))]
[JsonSerializable(typeof(HeroInfo[]))]
[JsonSerializable(typeof(HeroInfo))]
[JsonSerializable(typeof(ModEndpointInfo[]))]
[JsonSerializable(typeof(ModInfo[]))]
[JsonSerializable(typeof(SectorOwnership))]
internal sealed partial class WebUiJson : JsonSerializerContext;
