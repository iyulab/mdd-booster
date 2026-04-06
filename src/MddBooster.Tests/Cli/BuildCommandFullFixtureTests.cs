using MddBooster.Cli.Commands;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MddBooster.Tests.Cli;

/// <summary>
/// Cycle 27 — exercises the full BuildCommand pipeline on the
/// <c>order-with-derived</c> fixture so that enum, FK, VO, and
/// derived-field generation are validated end-to-end through the CLI,
/// not just via direct renderer calls. Every C# file produced is run
/// through the Roslyn parser to catch any syntax regression.
/// </summary>
public class BuildCommandFullFixtureTests
{
    [Fact]
    public void Full_pipeline_on_order_with_derived_produces_valid_artefacts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-full-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        var dbDir = Path.Combine(root, "src", "X.Database");
        var modelDir = Path.Combine(root, "src", "X.Entities");
        var apiDir = Path.Combine(root, "src", "X.Server");
        Directory.CreateDirectory(mddDir);
        Directory.CreateDirectory(dbDir);
        Directory.CreateDirectory(modelDir);
        Directory.CreateDirectory(apiDir);

        var fixtureSrc = Path.Combine(AppContext.BaseDirectory, "fixtures", "order-with-derived.m3l.md");
        File.Copy(fixtureSrc, Path.Combine(mddDir, "tables.m3l.md"));

        File.WriteAllText(Path.Combine(dbDir, "X.sqlproj"),
            """
            <Project Sdk="Microsoft.Build.Sql/0.2.5-preview">
              <PropertyGroup>
                <Name>X</Name>
                <DSP>Microsoft.Data.Tools.Schema.Sql.SqlAzureV12DatabaseSchemaProvider</DSP>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), """
{
  "sources": ["./tables.m3l.md"],
  "targets": [
    { "type": "Sql", "projectPath": "../src/X.Database", "schema": "dbo" },
    { "type": "Model", "projectPath": "../src/X.Entities", "namespace": "X.Entities", "dbContextName": "XDbContext" },
    { "type": "Api", "projectPath": "../src/X.Server", "namespace": "X.Server" }
  ]
}
""");

        try
        {
            var exitCode = new BuildCommand().Run(mddDir);
            Assert.Equal(0, exitCode);

            // Sanity spot-checks
            Assert.True(File.Exists(Path.Combine(dbDir, "dbo", "Tables_gen", "Order.sql")));
            Assert.True(File.Exists(Path.Combine(dbDir, "dbo", "Tables_gen", "Customer.sql")));
            Assert.True(File.Exists(Path.Combine(dbDir, "dbo", "Tables_gen", "OrderItem.sql")));

            Assert.True(File.Exists(Path.Combine(modelDir, "Enum_gen", "OrderStatus.cs")));
            Assert.True(File.Exists(Path.Combine(modelDir, "Entity_gen", "Order.cs")));
            Assert.True(File.Exists(Path.Combine(modelDir, "Entity_gen", "OrderExt.cs")));
            Assert.True(File.Exists(Path.Combine(modelDir, "DbContext_gen", "XDbContext.cs")));
            Assert.True(File.Exists(Path.Combine(apiDir, "Api_gen", "ApiRegistration_gen.cs")));

            // Enum CHECK constraint is in the Order.sql
            var orderSql = File.ReadAllText(Path.Combine(dbDir, "dbo", "Tables_gen", "Order.sql"));
            Assert.Contains("[Status] NVARCHAR(", orderSql);
            // CHECK constraints removed — EF Core handles enum validation
            Assert.DoesNotContain("CHECK", orderSql);
            Assert.Contains("REFERENCES [dbo].[Customer]([Id])", orderSql);
            // Derived fields must not land in the base table
            Assert.DoesNotContain("[CustomerName]", orderSql);
            Assert.DoesNotContain("[ItemCount]", orderSql);

            // OrderExt.cs contains all derived fields with their attributes
            var orderExt = File.ReadAllText(Path.Combine(modelDir, "Entity_gen", "OrderExt.cs"));
            Assert.Contains("public string CustomerName", orderExt);
            Assert.Contains("[global::Iyu.Core.Attributes.Lookup", orderExt);
            Assert.Contains("[global::Iyu.Core.Attributes.Rollup", orderExt);
            Assert.Contains("[global::Iyu.Core.Attributes.Computed", orderExt);
            Assert.Contains("Indexed = true", orderExt);

            // ApiRegistration emits a line for every entity
            var apiSrc = File.ReadAllText(Path.Combine(apiDir, "Api_gen", "ApiRegistration_gen.cs"));
            Assert.Contains("AddEntityPair<OrderExt, Order>(\"Orders\")", apiSrc);
            Assert.Contains("AddEntityPair<CustomerExt, Customer>(\"Customers\")", apiSrc);
            Assert.Contains("AddEntityPair<OrderItemExt, OrderItem>(\"OrderItems\")", apiSrc);

            // Parse every generated .cs file
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
            var allCs = Directory.GetFiles(modelDir, "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(apiDir, "*.cs", SearchOption.AllDirectories))
                .ToList();
            Assert.NotEmpty(allCs);
            foreach (var cs in allCs)
            {
                var src = File.ReadAllText(cs);
                var tree = CSharpSyntaxTree.ParseText(src, parseOptions);
                var errors = tree.GetDiagnostics()
                    .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
                Assert.True(errors.Count == 0,
                    $"Syntax errors in {Path.GetFileName(cs)}: {string.Join("; ", errors.Select(d => d.GetMessage()))}\n---\n{src}");
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
