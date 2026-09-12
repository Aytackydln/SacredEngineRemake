using System.Text.Json;
using System.Text;

namespace ParticleEmitterDataset;

internal static class JsonOutput
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        IncludeFields = true
    };

    public static void Write(string path, object value) => File.WriteAllText(path,
        JsonSerializer.Serialize(value, new JsonSerializerOptions(Options) { WriteIndented = true }) + "\n", new UTF8Encoding(false));

    public static void Lines<T>(string path, IEnumerable<T> values)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        foreach (var value in values) writer.WriteLine(JsonSerializer.Serialize(value, Options));
    }
}
