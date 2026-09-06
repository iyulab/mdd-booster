using System.Globalization;
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

    /// <summary>
    /// 2026-07-22 회귀 — 스펙 §10.8.1이 `@pk`를 `@primary`의 별칭으로 선언하는데
    /// 생성기가 `"pk"` 정확 일치만 검사해 `@primary` 모델이 PK 없는 DDL로 조용히 생성됨.
    /// </summary>
    [Fact]
    public void Render_PrimaryAliasIdField_EmitsPrimaryKeyLikePk()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("primary-alias.m3l.md"));
        var model = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Sample");
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

    /// <summary>
    /// An unquoted default that contains a colon reaches the DDL as it was written.
    /// The parser used to read such an argument as a key/value pair and rebuild it,
    /// which inserted a space after the colon — and that rebuilt value was rendered
    /// into the DEFAULT constraint, so a time literal shipped as a different time
    /// literal. Nothing between the parser and the DDL inspects the value, so this
    /// is the layer the guarantee has to be measured at.
    /// </summary>
    [Fact]
    public void Render_DefaultContainingAColon_KeepsTheValueAsWritten()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("colon-bearing-default.m3l.md"));
        var schedule = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Schedule");
        var cutoff = schedule.Fields.Single(f => f.Name == "cutoff");

        var line = ColumnRenderer.Render(cutoff);

        Assert.Contains("DEFAULT N'23:59:59'", line);
    }

    /// <summary>
    /// The DDL a model produces does not depend on the machine's locale.
    /// <para>
    /// This held even before the parse was made invariant, and it held for a reason worth
    /// stating: the numeric check only decides which branch is taken — the value written
    /// out is the source text either way — and every culture agrees on a literal like
    /// <c>1.5</c>. So there is no input this test would have caught. It is here to keep
    /// the property from being lost later, when someone renders the parsed number instead
    /// of the source text and the branch decision starts to matter.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void Render_NumericDefault_IsLocaleIndependent(string culture)
    {
        var ast = new M3lLoader().LoadFile(FixturePath("numeric-default.m3l.md"));
        var reading = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Reading");
        var rate = reading.Fields.Single(f => f.Name == "rate");

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var line = ColumnRenderer.Render(rate);

            Assert.Contains("DEFAULT 1.5", line);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
