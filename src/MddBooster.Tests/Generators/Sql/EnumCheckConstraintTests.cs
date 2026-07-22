using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// 2026-07-22 — enum CHECK 제약 opt-in 구현. 기본값 off(SSDT dacpac이 CHECK를
/// Drop→Create로 재현해 diff가 불안정 — cycle 27 정책 유지). 선언형(Schemorph 등)
/// 소비자는 <c>EmitEnumCheckConstraints</c>로 DB 레벨 enum 강제를 켤 수 있다.
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

        Assert.Contains(
            "CONSTRAINT [CK_Order_Status] CHECK ([Status] IN (N'draft', N'confirmed', N'in_production', N'shipped', N'cancelled'))",
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
