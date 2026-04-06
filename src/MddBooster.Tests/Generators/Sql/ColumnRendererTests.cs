using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

public class ColumnRendererTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static ResolvedModel LoadBankAccount()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("bank-account.m3l.md"));
        return new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "BankAccount");
    }

    [Fact]
    public void Render_IdField_EmitsUniqueidentifierPrimaryKeyWithDefault()
    {
        var model = LoadBankAccount();
        var id = model.Fields.Single(f => f.Name == "id");

        var line = ColumnRenderer.Render(id);

        Assert.Equal("[Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID()", line);
    }

    [Fact]
    public void Render_BankName_EmitsNvarchar30NotNull()
    {
        var model = LoadBankAccount();
        var f = model.Fields.Single(f => f.Name == "bank_name");

        var line = ColumnRenderer.Render(f);

        Assert.Equal("[BankName] NVARCHAR(30) NOT NULL", line);
    }

    [Fact]
    public void Render_NullableNote_EmitsNvarchar200Null()
    {
        var model = LoadBankAccount();
        var f = model.Fields.Single(f => f.Name == "note");

        var line = ColumnRenderer.Render(f);

        Assert.Equal("[Note] NVARCHAR(200) NULL", line);
    }

    [Fact]
    public void Render_IsActiveWithDefaultTrue_EmitsBitDefault1()
    {
        var model = LoadBankAccount();
        var f = model.Fields.Single(f => f.Name == "is_active");

        var line = ColumnRenderer.Render(f);

        Assert.Equal("[IsActive] BIT NOT NULL DEFAULT 1", line);
    }

    [Fact]
    public void Render_CreatedAtTimestampWithNowDefault_EmitsDatetimeoffsetSysdatetimeoffset()
    {
        var model = LoadBankAccount();
        var f = model.Fields.Single(f => f.Name == "created_at");

        var line = ColumnRenderer.Render(f);

        Assert.Equal("[CreatedAt] DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()", line);
    }
}
