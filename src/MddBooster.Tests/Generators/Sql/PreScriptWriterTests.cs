using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// A rename the model declares (<c>@renamed_from</c>) becomes a run-once script for a schema tool's stage before its
/// comparison — one file per step, oldest step first, never rewritten once written, and guarded so it does nothing
/// where the former name is gone.
/// </summary>
public sealed class PreScriptWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mdd-pre-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static IReadOnlyList<ResolvedModel> Load(string body)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-ren-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + body);
        try { return new InterfaceResolver(new M3lLoader().LoadFile(tmp)).ResolveAll(); }
        finally { File.Delete(tmp); }
    }

    private const string Model =
        "## CustomerOrder @renamed_from(SalesOrder)\n- id: identifier @pk @generated\n" +
        "- tier_code: string(20) @renamed_from(level, tier)\n- note: string(50)\n";

    [Fact]
    public void A_history_is_one_step_per_rename_table_first_oldest_first()
    {
        var steps = RenameSteps.Of(Load(Model));
        Assert.Equal(
            [
                (RenameSteps.Kind.Table, "SalesOrder", "CustomerOrder"),
                (RenameSteps.Kind.Column, "level", "tier"),
                (RenameSteps.Kind.Column, "tier", "tier_code"),
            ],
            steps.Select(s => (s.Kind, s.From, s.To)));
        Assert.All(steps, s => Assert.Equal("CustomerOrder", s.Model));
    }

    [Fact]
    public void Each_step_is_a_numbered_file_and_a_second_build_writes_nothing()
    {
        var writer = new PreScriptWriter(SqlDialect.PostgreSql, "public", n => PostgresIdentifiers.ToSnakeCase(n));
        var steps = RenameSteps.Of(Load(Model));
        Assert.Equal(
            ["V0001__rename_table_SalesOrder_to_CustomerOrder.sql", "V0002__rename_column_CustomerOrder_level_to_tier.sql",
             "V0003__rename_column_CustomerOrder_tier_to_tier_code.sql"],
            writer.Write(_dir, steps));
        var before = Directory.GetFiles(_dir).ToDictionary(f => f, File.ReadAllText);

        Assert.Empty(writer.Write(_dir, steps));
        Assert.Equal(before, Directory.GetFiles(_dir).ToDictionary(f => f, File.ReadAllText));
    }

    /// <summary>A rename declared later takes the next number after everything there, scripts written by hand included.</summary>
    [Fact]
    public void A_later_rename_is_appended_after_the_highest_number_in_the_directory()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "V0007__hand_written.sql"), "SELECT 1;");
        var writer = new PreScriptWriter(SqlDialect.SqlServer, "dbo", n => n);
        var written = writer.Write(_dir, RenameSteps.Of(Load("## Customer @renamed_from(Client)\n- id: identifier @pk\n")));
        Assert.Equal(["V0008__rename_table_Client_to_Customer.sql"], written);
    }

    [Fact]
    public void A_sql_server_script_renames_only_while_the_former_name_exists()
    {
        var writer = new PreScriptWriter(SqlDialect.SqlServer, "dbo", n => n);
        var steps = RenameSteps.Of(Load(Model));
        var table = writer.Render(steps[0]);
        Assert.Contains("IF OBJECT_ID(N'[dbo].[SalesOrder]', N'U') IS NOT NULL AND OBJECT_ID(N'[dbo].[CustomerOrder]', N'U') IS NULL", table);
        Assert.Contains("EXEC sp_rename N'[dbo].[SalesOrder]', N'CustomerOrder';", table);
        var column = writer.Render(steps[2]);
        Assert.Contains("COL_LENGTH(N'[dbo].[CustomerOrder]', N'Tier') IS NOT NULL AND COL_LENGTH(N'[dbo].[CustomerOrder]', N'TierCode') IS NULL", column);
        Assert.Contains("EXEC sp_rename N'[dbo].[CustomerOrder].[Tier]', N'TierCode', N'COLUMN';", column);
    }

    [Fact]
    public void A_postgres_script_renames_only_while_the_former_name_exists()
    {
        var writer = new PreScriptWriter(SqlDialect.PostgreSql, "public", n => PostgresIdentifiers.ToSnakeCase(n));
        var steps = RenameSteps.Of(Load(Model));
        var table = writer.Render(steps[0]);
        Assert.Contains("IF to_regclass('public.sales_order') IS NOT NULL AND to_regclass('public.customer_order') IS NULL THEN", table);
        Assert.Contains("ALTER TABLE public.sales_order RENAME TO customer_order;", table);
        Assert.Contains("ALTER TABLE public.customer_order RENAME COLUMN tier TO tier_code;", writer.Render(steps[2]));
    }

    /// <summary>
    /// The names this generator gave the table's constraints and indexes follow the table and the column — a table step
    /// renames the owner token under every name a column has had or has (a column renamed in the same change is still
    /// at its old name when the table step runs), and the column step then renames the column token.
    /// </summary>
    [Fact]
    public void Generated_constraint_and_index_names_follow_the_table_and_the_column()
    {
        const string model = "## CustomerOrder @renamed_from(SalesOrder)\n- id: identifier @pk @generated\n" +
                             "- order_code: string(20) @not_null @unique @renamed_from(code)\n";
        var steps = RenameSteps.Of(Load(model));

        var pg = new PreScriptWriter(SqlDialect.PostgreSql, "public", n => PostgresIdentifiers.ToSnakeCase(n));
        var pgTable = pg.Render(steps[0]);
        Assert.Contains("ALTER TABLE public.customer_order RENAME CONSTRAINT pk_sales_order TO pk_customer_order;", pgTable);
        Assert.Contains("ALTER TABLE public.customer_order RENAME CONSTRAINT uq_sales_order_code TO uq_customer_order_code;", pgTable);
        Assert.Contains("ALTER INDEX public.ix_sales_order_code RENAME TO ix_customer_order_code;", pgTable);
        Assert.Contains("ALTER TABLE public.customer_order RENAME CONSTRAINT uq_customer_order_code TO uq_customer_order_order_code;",
            pg.Render(steps[1]));

        var ts = new PreScriptWriter(SqlDialect.SqlServer, "dbo", n => n);
        var tsTable = ts.Render(steps[0]);
        Assert.Contains("EXEC sp_rename N'[dbo].[UK_SalesOrder_Code]', N'UK_CustomerOrder_Code', N'OBJECT';", tsTable);
        Assert.Contains("EXEC sp_rename N'[dbo].[CustomerOrder].[IX_SalesOrder_Code]', N'IX_CustomerOrder_Code', N'INDEX';", tsTable);
        Assert.DoesNotContain("PK_", tsTable); // T-SQL writes its primary and foreign keys unnamed
        Assert.Contains("EXEC sp_rename N'[dbo].[UK_CustomerOrder_Code]', N'UK_CustomerOrder_OrderCode', N'OBJECT';", ts.Render(steps[1]));
    }

    [Fact]
    public void A_model_without_renames_has_no_steps()
        => Assert.Empty(RenameSteps.Of(Load("## Plain\n- id: identifier @pk\n- name: string(10)\n")));
}
