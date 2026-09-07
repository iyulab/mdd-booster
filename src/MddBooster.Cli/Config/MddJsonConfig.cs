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

    /// <summary>
    /// Optional: module a generated form imports <c>FormSection</c>/<c>FormRow</c> from.
    /// Omit to keep the historical value.
    /// </summary>
    /// <remarks>
    /// These three settings exist because the generated file says
    /// <c>DO NOT EDIT</c>: a hard-coded specifier is not a default the consumer
    /// can override, it is a folder layout the consumer is required to build.
    /// The specifiers for this generator's <em>own</em> output are deliberately
    /// not settings — they are derived from <see cref="OutputPath"/> and
    /// <see cref="FormsOutputPath"/>, which the tool already knows.
    /// </remarks>
    [JsonPropertyName("formLayoutImport")]
    public string? FormLayoutImport { get; set; }

    /// <summary>
    /// Optional: module a generated form imports its controls
    /// (<c>UInput</c>·<c>UTextarea</c>·<c>USelect</c>·<c>UCheckbox</c>) from.
    /// Omit to keep the historical value.
    /// </summary>
    [JsonPropertyName("formControlsImport")]
    public string? FormControlsImport { get; set; }

    /// <summary>
    /// Optional: module supplying <c>enumToOptions</c>. This generator writes the
    /// <c>{Enum}Labels</c> maps that function consumes but not the function
    /// itself, so the module is the consumer's to provide.
    /// Omit to keep the historical value.
    /// </summary>
    [JsonPropertyName("formSelectOptionsImport")]
    public string? FormSelectOptionsImport { get; set; }

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

    /// <summary>
    /// TypeScript 타깃이 <b>폼만</b> 좁힐 화이트리스트. 생략하면 타깃 범위 전량(현행).
    /// <see cref="FormsExcludeEntities"/>와 함께 지정하면 빌드 오류.
    /// <para>
    /// <see cref="IncludeEntities"/>와는 **입도가 다르다**: 그쪽은 타깃의 엔티티 파생 산출물
    /// 전체(타입·필드 스키마·폼)에 걸리고, 이쪽은 <c>{Entity}Form_gen.tsx</c> 에만 걸린다.
    /// 엔티티 수 &gt; 화면 수인 소비앱 — 접합 테이블·감사 로그·부모 화면에 인라인으로 편집되는
    /// 자식처럼 단독 폼을 가질 수 없는 모델이 있는 쪽 — 이 타입은 전량 쓰면서 폼만 줄이는 자리다.
    /// </para>
    /// <para>
    /// 폼 집합은 언제나 타깃 집합의 <b>부분집합</b>이다. 생성된 폼이 자기 타입을
    /// <c>entities_gen.ts</c> 에서 임포트하므로, 타깃이 내지 않는 엔티티의 폼은 임포트가 깨진
    /// 파일이 된다 — 그 조합은 조용히 드롭하지 않고 설정 오류로 세운다.
    /// </para>
    /// </summary>
    [JsonPropertyName("formsInclude")]
    public List<string>? FormsInclude { get; set; }

    /// <summary>
    /// TypeScript 타깃이 폼에서 제외할 블랙리스트. <see cref="FormsInclude"/>의 반대 방향이며,
    /// 같은 부분집합 규칙과 검증을 따른다.
    /// </summary>
    [JsonPropertyName("formsExclude")]
    public List<string>? FormsExclude { get; set; }

    /// <summary>
    /// Api 타깃이 방출할 권한 키의 형식. 지정하면 <c>Api_gen/Permissions_gen.cs</c> 가 함께
    /// 생성되고, 생략하면(기본) 아무것도 방출하지 않는다.
    /// <para>
    /// 자리표시자: <c>{entitySet}</c>(예 <c>Orders</c>) · <c>{entitySetLower}</c>(<c>orders</c>) ·
    /// <c>{verb}</c>(<c>read</c>/<c>write</c>) · <c>{Verb}</c>(<c>Read</c>/<c>Write</c>).
    /// 예: <c>"{entitySetLower}.{verb}"</c> → <c>orders.read</c>. 알 수 없는 자리표시자는 빌드 오류다
    /// — 그대로 통과시키면 어떤 정책과도 매칭되지 않는 리터럴 키가 조용히 나간다.
    /// </para>
    /// <para>
    /// 🔴 값이 «입력»인 것이 요점이다. 키 형식은 소비앱의 인가 규약이라, 생성기가 정하면 규약이
    /// 다른 소비자에게 «틀린» 상수가 간다.
    /// </para>
    /// </summary>
    [JsonPropertyName("permissionKeyTemplate")]
    public string? PermissionKeyTemplate { get; set; }
}
