using MddBooster.Cli.Commands;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MddBooster.Tests.Cli;

/// <summary>
/// Cycle 32 — runs the full BuildCommand against Yesung's real
/// <c>tables.m3l.md</c> (14 entities + 13 enums). This is the Plan 5
/// acceptance gate: if this passes, the generator can produce Yesung's
/// complete domain model in one pass.
/// </summary>
public class YesungFullFixtureTests
{
    private static readonly string YesungTablesPath =
        @"D:\data\yesung\mdd\tables.m3l.md";

    [Fact]
    public void Yesung_tables_m3l_md_generates_without_errors()
    {
        if (!File.Exists(YesungTablesPath))
            return; // Skip if fixture not present (CI / fresh clone)

        var root = Path.Combine(Path.GetTempPath(), $"yesung-full-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        var dbDir = Path.Combine(root, "src", "Y.Database");
        var modelDir = Path.Combine(root, "src", "Y.Entities");
        var apiDir = Path.Combine(root, "src", "Y.Server");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(dbDir);
        Directory.CreateDirectory(modelDir);
        Directory.CreateDirectory(apiDir);

        File.Copy(YesungTablesPath, Path.Combine(mddDir, "tables.m3l.md"));
        File.WriteAllText(Path.Combine(dbDir, "Y.sqlproj"),
            """
            <Project Sdk="Microsoft.Build.Sql/0.2.5-preview">
              <PropertyGroup><Name>Y</Name></PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
{
  "sources": ["./tables.m3l.md"],
  "targets": [
    { "type": "Sql", "projectPath": "../src/Y.Database", "schema": "dbo" },
    { "type": "Model", "projectPath": "../src/Y.Entities", "namespace": "Yesung.Entities", "dbContextName": "YesungDbContext" },
    { "type": "Api", "projectPath": "../src/Y.Server", "namespace": "Yesung.Server" }
  ]
}
""");

        try
        {
            var exit = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exit);

            // Validate every generated .cs file parses as valid C#
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
            var allCs = Directory.GetFiles(modelDir, "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(apiDir, "*.cs", SearchOption.AllDirectories))
                .ToList();
            Assert.True(allCs.Count >= 14 + 13, $"Expected at least 14 entity files + 13 enum files, got {allCs.Count}");

            foreach (var cs in allCs)
            {
                var src = File.ReadAllText(cs);
                var tree = CSharpSyntaxTree.ParseText(src, parseOptions);
                var errors = tree.GetDiagnostics()
                    .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
                Assert.True(errors.Count == 0,
                    $"Syntax errors in {Path.GetFileName(cs)}: {string.Join("; ", errors.Select(d => d.GetMessage()))}");
            }

            // Validate every generated .sql file is non-empty
            var allSql = Directory.GetFiles(dbDir, "*.sql", SearchOption.AllDirectories);
            Assert.NotEmpty(allSql);
            foreach (var sql in allSql)
                Assert.True(new FileInfo(sql).Length > 0);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
