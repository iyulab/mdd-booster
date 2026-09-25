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
