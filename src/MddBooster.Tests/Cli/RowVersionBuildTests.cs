using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// A <c>rowversion</c> field — the row version the database engine maintains — carried through
/// every target, for both dialects. SQL Server stores it in a <c>rowversion</c> column; PostgreSQL
/// has none and keeps it in the <c>xmin</c> system column, so the two dialects diverge in the
/// table, the views, the CLR type and the column mapping, and agree everywhere else.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class RowVersionBuildTests
{
    // Customer: no view. Booking: a lookup, read through a FullView over the table. Invoice:
    // soft-deleted with a lookup, so its FullView reads through the UdView rather than the table.
    private const string Model = """
# Namespace: X

## Timestampable ::interface
- created_at: timestamp = now()
- updated_at: timestamp = now()

## Customer : Timestampable
- id: identifier @pk @generated
- name: string(50)
- row_version: rowversion

## Booking : Timestampable
- id: identifier @pk @generated
- customer_id: identifier @reference(Customer)
- customer_name: string(50) @lookup(customer_id.name)
- row_version: rowversion

## Invoice : Timestampable
- id: identifier @pk @generated
- customer_id: identifier @reference(Customer)
- customer_name: string(50) @lookup(customer_id.name)
- deleted_at: timestamp?
- row_version: rowversion
""";

    private sealed record Output(string Root) : IDisposable
    {
        public string Read(params string[] path) => File.ReadAllText(Path.Combine([Root, .. path]));

        public bool Exists(params string[] path) => File.Exists(Path.Combine([Root, .. path]));

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Output Build(string dialect, string model = Model, int expectedExit = 0)
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-rowversion-{dialect}-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        foreach (var d in new[] { "db", "entities", "ui" })
            Directory.CreateDirectory(Path.Combine(root, d));

        File.WriteAllText(Path.Combine(mddDir, "model.m3l.md"), model);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), $$"""
{
  "sources": ["./model.m3l.md"],
  "targets": [
    { "type": "Sql", "dialect": "{{dialect}}", "projectPath": "../db", "emitSqlProj": false },
    { "type": "Model", "dialect": "{{dialect}}", "projectPath": "../entities", "namespace": "X.Entities", "dbContextName": "XDbContext" },
    { "type": "TypeScript", "outputPath": "../ui/types", "formsOutputPath": "../ui/forms",
      "formLayoutImport": "@example/layout", "formControlsImport": "@example/controls",
      "formSelectOptionsImport": "@example/enum-options" }
  ]
}
""");

        var output = new Output(root);
        Assert.Equal(expectedExit, new BuildCommand().Run(mddDir));
        return output;
    }

    [Fact]
    public void Sql_server_stores_the_row_version_in_a_rowversion_column()
    {
        using var o = Build("tsql");

        Assert.Contains("[RowVersion] ROWVERSION NOT NULL", o.Read("db", "dbo", "Tables_gen", "Customer.sql"));
    }

    [Fact]
    public void Postgres_creates_no_column_and_its_views_project_xmin_under_the_field_name()
    {
        using var o = Build("postgres");

        Assert.DoesNotContain("row_version", o.Read("db", "tables_gen", "customer.sql"));

        // Read from the table: the system column, renamed.
        Assert.Contains("b.xmin AS row_version", o.Read("db", "views_gen", "booking_full_view.sql"));
        Assert.Contains("b.xmin AS row_version", o.Read("db", "views_gen", "invoice_ud_view.sql"));

        // Read through the UdView, which has no system columns and already carries the name.
        var invoiceFull = o.Read("db", "views_gen", "invoice_full_view.sql");
        Assert.Contains("b.row_version", invoiceFull);
        Assert.DoesNotContain("xmin", invoiceFull);
    }

    [Theory]
    [InlineData("tsql", "public byte[] RowVersion { get; set; } = Array.Empty<byte>();")]
    [InlineData("postgres", "public uint RowVersion { get; set; }")]
    public void Both_entity_classes_carry_the_row_version_as_a_timestamp(string dialect, string property)
    {
        using var o = Build(dialect);

        foreach (var file in new[] { "Booking.cs", "BookingExt.cs" })
        {
            var cs = o.Read("entities", "Entity_gen", file);
            Assert.Contains("    [Timestamp]\n    " + property, cs.ReplaceLineEndings("\n"));
            // The engine sets it, so nothing about it is the caller's to supply.
            Assert.DoesNotContain("[Required]\n    " + property, cs.ReplaceLineEndings("\n"));
        }
    }

    [Fact]
    public void Postgres_maps_the_row_version_to_xmin_where_the_table_is_read_and_to_the_view_column_elsewhere()
    {
        using var o = Build("postgres");
        var ctx = o.Read("entities", "DbContext_gen", "XDbContext.cs").ReplaceLineEndings("\n");

        static string Block(string ctx, string entity)
        {
            var start = ctx.IndexOf($"modelBuilder.Entity<{entity}>(e =>", StringComparison.Ordinal);
            Assert.True(start >= 0, entity);
            return ctx[start..ctx.IndexOf("});", start, StringComparison.Ordinal)];
        }

        const string Xmin = "e.Property(x => x.RowVersion).HasColumnName(\"xmin\");";
        const string Named = "e.Property(x => x.RowVersion).HasColumnName(\"row_version\");";

        Assert.Contains(Xmin, Block(ctx, "Booking"));         // write type — the table
        Assert.Contains(Named, Block(ctx, "BookingExt"));     // read type — booking_full_view
        Assert.Contains(Xmin, Block(ctx, "Customer"));
        Assert.Contains(Xmin, Block(ctx, "CustomerExt"));     // read type with no view — the table
    }

    [Fact]
    public void TypeScript_types_it_opaquely_marks_it_read_only_and_leaves_it_out_of_forms()
    {
        using var o = Build("tsql");

        Assert.Contains("RowVersion: string | number", o.Read("ui", "types", "entities_gen.ts"));

        var schema = o.Read("ui", "types", "field_schema_gen.ts").ReplaceLineEndings("\n");
        var customer = schema[schema.IndexOf("  Customer: {", StringComparison.Ordinal)..];
        customer = customer[..customer.IndexOf("\n  },", StringComparison.Ordinal)];
        var line = customer.Split('\n').Single(l => l.TrimStart().StartsWith("RowVersion:", StringComparison.Ordinal));
        Assert.Contains("readOnly: true", line);
        Assert.DoesNotContain("required", line);

        Assert.DoesNotContain("RowVersion", o.Read("ui", "forms", "CustomerForm_gen.tsx"));
    }

    [Fact]
    public void Postgres_refuses_an_index_on_the_row_version_instead_of_emitting_ddl_that_cannot_apply()
    {
        const string indexed = """
# Namespace: X

## Timestampable ::interface
- created_at: timestamp = now()
- updated_at: timestamp = now()

## Customer : Timestampable
- id: identifier @pk @generated
- row_version: rowversion @index
""";
        using var stderr = new ConsoleErrorCapture(this);
        using var o = Build("postgres", indexed, expectedExit: 3);

        Assert.Contains("row_version", stderr.Text);
        Assert.Contains("xmin", stderr.Text);
    }
}
