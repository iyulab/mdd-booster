using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// 2026-07-22 — enum CHECK 제약 opt-in 구현. 기본값 off(SSDT dacpac이 CHECK를
/// Drop→Create로 재현해 diff가 불안정 — cycle 27 정책 유지). 선언형(Schemorph 등)
/// 소비자는 <c>EmitEnumCheckConstraints</c>로 DB 레벨 enum 강제를 켤 수 있다.
/// <para>
/// 2026-08-18 — 그 불안정의 근본 원인이 실측으로 확인됐다: SQL Server는 `CHECK (col
/// IN (…))`을 문자 그대로 저장하지 않고 역순 OR 체인으로 재작성한다(`sys.check_
/// constraints.definition`이 그렇게 반환됨). 선언형 비교 도구가 원본 `IN(…)` 소스와
/// 재작성된 라이브 정의를 문자/구조 비교하면 영원히 "다르다"로 본다. 생성기가 처음부터
/// SQL Server가 저장할 그 모양(역순 OR 체인)으로 방출하도록 고쳤다 — 자세한 근거는
/// <see cref="EnumSqlConvention.CheckExpression"/> 문서 참조.
/// </para>
/// </summary>
public class EnumCheckConstraintTests
{
    private static (ResolvedModel model, IReadOnlyDictionary<string, M3L.Native.EnumNode> enums) LoadOrder()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "order-with-enum.m3l.md"));
        var model = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");
        var enums = ast.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal);
        return (model, enums);
    }

    [Fact]
    public void Default_emits_no_check_constraint()
    {
        var (model, enums) = LoadOrder();

        var sql = TableRenderer.Render(model, "dbo", enums);

        Assert.DoesNotContain("CHECK", sql);
    }

    [Fact]
    public void OptIn_emits_table_level_check_constraint_per_enum_column()
    {
        var (model, enums) = LoadOrder();

        var sql = TableRenderer.Render(model, "dbo", enums, emitEnumCheckConstraints: true);

        // Reverse declaration order, one `[Status]=…` per value — the exact shape SQL
        // Server itself rewrites `IN (…)` into (see EnumSqlConvention.CheckExpression).
        Assert.Contains(
            "CONSTRAINT [CK_Order_Status] CHECK ([Status]=N'cancelled' OR [Status]=N'shipped' OR [Status]=N'in_production' OR [Status]=N'confirmed' OR [Status]=N'draft')",
            sql);
    }

    [Fact]
    public void Enum_column_width_is_sized_to_longest_member_with_floor_20()
    {
        var (model, enums) = LoadOrder();

        var sql = TableRenderer.Render(model, "dbo", enums);

        // in_production(13자) < 20 → 하한 20 적용.
        Assert.Contains("[Status] NVARCHAR(20)", sql);
    }
}
