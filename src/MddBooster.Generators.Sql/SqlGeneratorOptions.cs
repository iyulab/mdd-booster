namespace MddBooster.Generators.Sql;

public sealed class SqlGeneratorOptions
{
    /// <summary>
    /// .sqlproj 파일이 있는 프로젝트 루트 경로.
    /// 상대 경로이면 GeneratorContext.WorkingDirectory 기준으로 해석.
    /// </summary>
    public required string ProjectPath { get; init; }

    /// <summary>
    /// T-SQL 스키마 이름 (예: "dbo")
    /// </summary>
    public string Schema { get; init; } = "dbo";
}
