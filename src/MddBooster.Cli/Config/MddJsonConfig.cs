using System.Text.Json.Serialization;

namespace MddBooster.Cli.Config;

public sealed class MddJsonConfig
{
    [JsonPropertyName("sources")]
    public List<string> Sources { get; set; } = [];

    [JsonPropertyName("targets")]
    public List<MddJsonTarget> Targets { get; set; } = [];
}

public sealed class MddJsonTarget
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("projectPath")]
    public string ProjectPath { get; set; } = "";

    // Sql target
    /// <summary>
    /// Sql target: SQL dialect. <c>"tsql"</c> (default — current SSDT/T-SQL behavior) or
    /// <c>"postgres"</c> (snake_case identifiers per naming gates, <c>tables_gen/</c> layout,
    /// scope aligned with Schemorph PG capabilities). Unknown values are a build error.
    /// </summary>
    [JsonPropertyName("dialect")]
    public string? Dialect { get; set; }

    [JsonPropertyName("schema")]
    public string? Schema { get; set; }

    /// <summary>
    /// Sql target: whether to patch the <c>.sqlproj</c> ItemGroup with generated files.
    /// Set <c>false</c> when consuming schema via a desired-state tool (e.g. Schemorph) so the
    /// <c>.sqlproj</c> can be retired. When omitted, defaults to <c>true</c> (current behavior).
    /// </summary>
    [JsonPropertyName("emitSqlProj")]
    public bool? EmitSqlProj { get; set; }

    /// <summary>
    /// Sql target: whether to emit the post-deployment <c>RefreshViews.sql</c> (sp_refreshview).
    /// Independent of <see cref="EmitSqlProj"/>. When omitted, defaults to <c>true</c>.
    /// </summary>
    [JsonPropertyName("emitRefreshScript")]
    public bool? EmitRefreshScript { get; set; }

    /// <summary>
    /// Sql target: whether to emit table-level <c>CK_{Table}_{Column}</c> CHECK constraints
    /// for enum columns. Defaults to <c>false</c> (SSDT dacpac represents CHECK as
    /// Drop→Create on every diff). Declarative-tool consumers (Schemorph) can opt in
    /// for DB-level enum enforcement.
    /// </summary>
    [JsonPropertyName("emitEnumCheckConstraints")]
    public bool? EmitEnumCheckConstraints { get; set; }

    /// <summary>
    /// Sql target: whether to create an index on every foreign-key column the model has
    /// not already covered. Defaults to <c>false</c>. Neither engine indexes a foreign key
    /// on its own, so joins and delete-time referencing checks scan the child table;
    /// turning this on trades write and storage cost for those reads.
    /// </summary>
    [JsonPropertyName("emitForeignKeyIndexes")]
    public bool? EmitForeignKeyIndexes { get; set; }

    // Model target
    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("dbContextName")]
    public string? DbContextName { get; set; }

    /// <summary>
    /// Optional: path to the SSDT SQL project root (relative to mdd.json or absolute).
    /// When set on a Model target, the generator scans <c>dbo/Views/</c> for
    /// <c>{Name}ExtView.sql</c> files to use as the highest-priority read backing.
    /// </summary>
    [JsonPropertyName("sqlProjectPath")]
    public string? SqlProjectPath { get; set; }

    // TypeScript target
    [JsonPropertyName("outputPath")]
    public string? OutputPath { get; set; }

    /// <summary>
    /// Optional path for generated {Entity}Form_gen.tsx files (TypeScript target only).
    /// When omitted, form generation is skipped.
    /// </summary>
    [JsonPropertyName("formsOutputPath")]
    public string? FormsOutputPath { get; set; }

    // Api target
    /// <summary>
    /// Api 타깃이 참조할 엔티티 타입의 namespace (생성 코드의 <c>using</c>). 생략하면
    /// Model 타깃이 **유일할 때만** 그 namespace로 추론한다.
    /// <para>
    /// Model 타깃이 둘 이상이면 추론하지 않고 빌드 오류로 명시를 요구한다 — 과거에는 첫 Model
    /// 타깃을 집어 나머지를 조용히 무시했고, 그 결과 Api 타깃이 잘못된 <c>using</c>을 방출해
    /// 소비자 빌드가 깨졌다.
    /// </para>
    /// </summary>
    [JsonPropertyName("entitiesNamespace")]
    public string? EntitiesNamespace { get; set; }

    // Api / TypeScript targets — 타깃별 엔티티 부분집합
    /// <summary>
    /// 표면 타깃(Api·TypeScript)이 방출할 엔티티 화이트리스트. 생략하면 전량(현행).
    /// <see cref="ExcludeEntities"/>와 함께 지정하면 빌드 오류.
    /// <para>
    /// 한 모델 정본을 여러 서버가 소비할 때 "이 서버는 생산 관련 엔티티만 노출"을 표현한다.
    /// Sql·Model 타깃에는 지정할 수 없다 — FK/상속 무결성이 깨진다.
    /// </para>
    /// <para>
    /// ⚠️ 화이트리스트는 drift 한다: 정본에 새 엔티티가 추가돼도 이 타깃에는 **조용히**
    /// 나타나지 않는다. 신규 엔티티가 기본 노출되기를 원하면 <see cref="ExcludeEntities"/>를 쓸 것.
    /// (빌드마다 커버리지 회계를 콘솔에 출력해 무엇이 빠졌는지는 보이게 한다.)
    /// </para>
    /// </summary>
    [JsonPropertyName("includeEntities")]
    public List<string>? IncludeEntities { get; set; }

    /// <summary>
    /// 표면 타깃(Api·TypeScript)이 제외할 엔티티 블랙리스트. 생략하면 전량(현행).
    /// <see cref="IncludeEntities"/>와 함께 지정하면 빌드 오류.
    /// </summary>
    [JsonPropertyName("excludeEntities")]
    public List<string>? ExcludeEntities { get; set; }
}
