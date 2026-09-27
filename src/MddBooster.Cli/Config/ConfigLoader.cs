using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MddBooster.Core.Naming;

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
        MddJsonConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<MddJsonConfig>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ConfigException(Describe(path, ex), path, ex);
        }
        if (cfg is null)
        {
            throw new ConfigException($"mdd.json 이 설정 객체가 아닙니다({path}) — 최상위가 {{ \"sources\": [...], \"targets\": [...] }} 형태여야 합니다.", path);
        }
        return cfg;
    }

    /// <summary>
    /// 설정이 정의하지 않은 키마다 한 문장 — 위치(<c>targets[1].projectpth</c>), 가까운 알려진 키가 있으면
    /// 그 제안, 그리고 그 자리의 알려진 키 목록. 키는 여전히 무시된다(새 판의 키를 옛 판이 거절하지 않게)
    /// — 달라지는 것은 말없이 버리지 않는다는 것이다.
    /// </summary>
    /// <remarks>
    /// 알려진 키는 설정 타입의 <see cref="JsonPropertyNameAttribute"/> 에서 읽는다 — 스키마 드리프트
    /// 시험이 같은 원천을 스키마와 대조하므로, 목록을 여기 따로 적으면 셋째 사본이 된다.
    /// </remarks>
    public static IReadOnlyList<string> UnknownKeyWarnings(MddJsonConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var warnings = new List<string>();
        Collect(config.UnknownKeys, "", KnownKeys<MddJsonConfig>(), warnings);
        for (var i = 0; i < config.Targets.Count; i++)
            Collect(config.Targets[i].UnknownKeys, $"targets[{i}].", KnownKeys<MddJsonTarget>(), warnings);
        return warnings;
    }

    private static void Collect(Dictionary<string, JsonElement>? unknown, string prefix,
        IReadOnlyList<string> known, List<string> warnings)
    {
        if (unknown is null) return;
        foreach (var key in unknown.Keys.Order(StringComparer.Ordinal))
        {
            var hint = EditDistance.Nearest(key, known, maxDistance: 2) is { } near ? $" — '{near}' 의 오타일 수 있습니다" : "";
            warnings.Add($"mdd.json 의 '{prefix}{key}' 는 알 수 없는 키라 무시됩니다{hint}. 이 자리의 키: {string.Join(", ", known)}");
        }
    }

    private static IReadOnlyList<string> KnownKeys<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .OfType<string>()
            .ToList();

    /// <summary>
    /// JSON 파서의 오류를 사용자가 고칠 자리로: 파일 · 행 · 항목 경로를 먼저, 그다음 원인. 값의 형식이
    /// 틀린 경우는 .NET 타입 이름 대신 설정에서 쓰는 말(문자열 · 배열 …)로 말하고, 문법 오류는 파서의
    /// 원문을 그대로 두되 그것이 파서의 말임을 밝힌다 — 파서가 무엇을 기대했는지는 파서가 가장 정확하다.
    /// </summary>
    internal static string Describe(string path, JsonException ex)
    {
        var where = ex.LineNumber is { } line
            ? $"{line + 1}행" + (ex.BytePositionInLine is { } column ? $" {column + 1}열" : "")
            : "위치 불명";
        var member = string.IsNullOrEmpty(ex.Path) || ex.Path == "$" ? "" : $", 항목 {ex.Path.TrimStart('$', '.')}";

        var reason = ex.Message;
        var cut = reason.IndexOf(" Path: ", StringComparison.Ordinal);
        if (cut >= 0) reason = reason[..cut];

        const string ConversionPrefix = "The JSON value could not be converted to ";
        var explained = reason.StartsWith(ConversionPrefix, StringComparison.Ordinal)
            ? $"값의 형식이 맞지 않습니다 — 기대: {Expected(reason[ConversionPrefix.Length..].TrimEnd('.'))}."
            : $"JSON 문법 오류(파서 원문: {reason})";

        return $"mdd.json 을 읽을 수 없습니다 — {path} {where}{member}: {explained}";
    }

    private static string Expected(string clrType) => clrType switch
    {
        "System.String" => "문자열",
        "System.Boolean" => "true 또는 false",
        "System.Int32" or "System.Int64" => "정수",
        _ when clrType.StartsWith("System.Collections.Generic.List`1[System.String]", StringComparison.Ordinal)
            => "문자열 배열",
        _ when clrType.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal)
            => "배열",
        _ => "객체",
    };
}
