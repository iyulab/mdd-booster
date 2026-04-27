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

    /// <summary>
    /// 2026-04-27 회귀 — m3l `field: EnumType = "value"` 정의에서 default가 SQL에 emit되지 않아
    /// SSDT publish 시 NOT NULL 컬럼 신규 추가에서 Msg 515 발생.
    /// 파서가 quote를 제거한 raw value(`product`)를 enum 컬럼에서도 N'product'로 emit해야 함.
    /// </summary>
    [Fact]
    public void Render_EnumFieldWithDefault_EmitsNvarcharWithQuotedValue()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("enum-default.m3l.md"));
        var item = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Item");
        var lookup = ast.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var statusField = item.Fields.Single(f => f.Name == "status");

        var line = ColumnRenderer.Render(statusField, lookup);

        // Status = "draft" → DEFAULT N'draft'
        Assert.Contains("DEFAULT N'draft'", line);
    }

    /// <summary>
    /// string 타입 default도 동일 정책 — quote 없는 raw value를 N'value'로 emit.
    /// </summary>
    [Fact]
    public void Render_StringFieldWithDefault_EmitsNvarcharWithQuotedValue()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("enum-default.m3l.md"));
        var item = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Item");
        var codeField = item.Fields.Single(f => f.Name == "code");

        var line = ColumnRenderer.Render(codeField);

        Assert.Contains("DEFAULT N'TBD'", line);
    }
}
