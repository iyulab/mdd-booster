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
}
