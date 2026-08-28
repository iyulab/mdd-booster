using System.Text.Json;
using Json.Schema;

namespace MddBooster.Tests.Config;

/// <summary>
/// Ratchets `schemas/mdd.schema.json` against the actual mdd.json shape — same reasoning as
/// <c>FormControlContractRatchetTests</c>: reads the published files themselves, not a copy kept
/// in test code, so the two cannot silently drift apart while this test keeps passing.
/// Regression target: docket-reported case where a target-level option
/// (<c>emitForeignKeyIndexes</c>) placed at the config root was silently ignored by
/// <c>ConfigLoader</c> instead of surfacing as an error.
/// </summary>
public class MddSchemaTests
{
    private static readonly JsonSchema Schema = JsonSchema.FromFile(
        Path.Combine(AppContext.BaseDirectory, "contract", "mdd.schema.json"));

    private static EvaluationResults Validate(string json)
        => Schema.Evaluate(JsonDocument.Parse(json).RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

    private static string DescribeErrors(EvaluationResults result)
        => string.Join("; ", (result.Details ?? [])
            .Where(d => !d.IsValid)
            .Select(d => d.EvaluationPath + ": " + string.Join(",", d.Errors?.Values ?? Enumerable.Empty<string>())));

    [Fact]
    public void Published_sample_validates_against_the_schema()
    {
        var samplePath = Path.Combine(AppContext.BaseDirectory, "contract", "samples-mdd.json");
        var result = Validate(File.ReadAllText(samplePath));
        Assert.True(result.IsValid, DescribeErrors(result));
    }

    [Fact]
    public void Target_level_option_left_at_the_config_root_is_rejected()
    {
        // The exact silent-ignore shape ConfigLoader let through — this is the schema's reason
        // to exist. See ISSUE-mdd-booster-20260827-config-loader-raw-exception.md / ROADMAP §1.
        const string json = """
        {
          "sources": ["./tables.m3l.md"],
          "emitForeignKeyIndexes": true,
          "targets": [ { "type": "Sql", "projectPath": "./db" } ]
        }
        """;
        Assert.False(Validate(json).IsValid);
    }

    [Fact]
    public void Unknown_property_on_a_target_is_rejected()
    {
        const string json = """
        {
          "sources": ["./tables.m3l.md"],
          "targets": [ { "type": "Api", "projectPath": "./api", "namespace": "N", "dialect": "postgres" } ]
        }
        """;
        Assert.False(Validate(json).IsValid);
    }

    [Fact]
    public void Postgres_dialect_with_sqlproj_knobs_is_rejected()
    {
        // Mirrors the runtime InvalidOperationException in BuildCommand.CreateSqlGenerator —
        // the schema should catch this before a build even runs.
        const string json = """
        {
          "sources": ["./tables.m3l.md"],
          "targets": [ { "type": "Sql", "projectPath": "./db", "dialect": "postgres", "emitSqlProj": true } ]
        }
        """;
        Assert.False(Validate(json).IsValid);
    }

    [Fact]
    public void IncludeEntities_and_excludeEntities_together_is_rejected()
    {
        const string json = """
        {
          "sources": ["./tables.m3l.md"],
          "targets": [ { "type": "Api", "projectPath": "./api", "namespace": "N",
                         "includeEntities": ["Order"], "excludeEntities": ["User"] } ]
        }
        """;
        Assert.False(Validate(json).IsValid);
    }

    [Fact]
    public void Unsupported_target_type_is_rejected()
    {
        const string json = """
        {
          "sources": ["./tables.m3l.md"],
          "targets": [ { "type": "GraphQL", "projectPath": "./x" } ]
        }
        """;
        Assert.False(Validate(json).IsValid);
    }

    [Fact]
    public void Full_four_target_config_with_every_documented_option_validates()
    {
        const string json = """
        {
          "sources": ["./a.m3l.md", "./b.m3l.md"],
          "targets": [
            { "type": "Sql", "projectPath": "./db", "dialect": "postgres", "schema": "public",
              "emitEnumCheckConstraints": true, "emitForeignKeyIndexes": false },
            { "type": "Model", "projectPath": "./ent", "namespace": "N.Entities",
              "dbContextName": "NDbContext", "sqlProjectPath": "./db", "dialect": "postgres" },
            { "type": "Api", "projectPath": "./api", "namespace": "N.Server",
              "entitiesNamespace": "N.Entities", "excludeEntities": ["Secret"] },
            { "type": "TypeScript", "outputPath": "./ts", "formsOutputPath": "./forms",
              "formLayoutImport": "@x/enterprise", "formControlsImport": "../ui",
              "formSelectOptionsImport": "../select", "includeEntities": ["Order"] }
          ]
        }
        """;
        var result = Validate(json);
        Assert.True(result.IsValid, DescribeErrors(result));
    }
}
