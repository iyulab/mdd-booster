using System.Text.Json;

namespace MddBooster.Cli.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public static MddJsonConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"mdd.json을 찾을 수 없습니다: {path}", path);
        }

        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<MddJsonConfig>(json, Options);
        if (cfg is null)
        {
            throw new InvalidDataException($"mdd.json 역직렬화 실패: {path}");
        }
        return cfg;
    }
}
