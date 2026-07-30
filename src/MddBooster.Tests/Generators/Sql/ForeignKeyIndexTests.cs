using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;
using MddBooster.Generators.Sql.Postgres;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// `emitForeignKeyIndexes` — the opt-in that indexes foreign-key columns the model has
/// not covered itself.
/// </summary>
/// <remarks>
/// The gap it closes is present on both engines: neither indexes a foreign key by
/// convention, so a join through it — and the referencing check a delete performs — scans
/// the child table. What the two dialects disagree on is how an index is written, not
/// which columns want one, so the decision is asserted against both renderers.
/// <para>
/// These run against the acceptance model because the interesting cases are the ones
/// where a foreign key is <em>already</em> covered, and that needs a model carrying
/// declared indexes, composite uniques and plain references side by side.
/// </para>
/// </remarks>
public class ForeignKeyIndexTests
{
    private static readonly Lazy<(IReadOnlyList<ResolvedModel> Models,
                                  IReadOnlyDictionary<string, ResolvedModel> Lookup,
                                  IReadOnlyDictionary<string, string> TableNames,
                                  IReadOnlyDictionary<string, EnumNode> Enums)> Model = new(() =>
    {
        var ast = new M3lLoader().LoadFile(AcceptanceModel.FixturePath);
        var models = new InterfaceResolver(ast).ResolveAll().ToList();
        return (models,
                models.ToDictionary(m => m.Name, StringComparer.Ordinal),
                PostgresIdentifiers.BuildTableNameMap(models.Select(m => m.Name)),
                ast.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal));
    });

    private static string Tsql(ResolvedModel model, bool on) =>
        TableRenderer.Render(model, "dbo", Model.Value.Enums, false, emitForeignKeyIndexes: on);

    private static string Pg(ResolvedModel model, bool on) =>
        PgTableRenderer.Render(model, "public", Model.Value.TableNames, Model.Value.Lookup,
            Model.Value.Enums, false, emitForeignKeyIndexes: on).Sql;

    private static IReadOnlyList<string> Planned(ResolvedModel model) =>
        ForeignKeyIndexPlanner.Plan(model)
            .Select(f => f.Name).ToList();

    // ---- 기본값은 꺼져 있다 — 기존 산출물이 바뀌지 않는다 ----

    [Fact]
    public void Default_emits_only_the_indexes_the_model_declares()
    {
        // 기본값이 켜져 있으면 기존 소비자의 스키마 diff 에 인덱스가 한꺼번에 나타나고,
        // 선언형 적용 도구가 그것을 그대로 적용한다.
        //
        // 이 단정은 **절대 기준**이어야 한다. 기본값 산출물을 인자 없는 호출과 비교하는 형태로
        // 쓰면 둘이 같은 코드를 지나므로 규칙이 항상 켜지도록 바뀌어도 함께 움직여 통과한다
        // (변이 검사로 실제로 확인된 자기참조였다).
        var (models, lookup, tableNames, enums) = Model.Value;

        var declared = models
            .SelectMany(m => ForeignKeyIndexPlanner.Plan(m).Select(f => (Model: m, Field: f)))
            .ToList();
        Assert.NotEmpty(declared);   // 대상이 하나도 없으면 아래 단정이 공허해진다

        foreach (var (model, field) in declared)
        {
            var column = NameCasing.ToPascalCase(field.Name);
            Assert.DoesNotContain($"[IX_{model.Name}_{column}]", Tsql(model, on: false));
            Assert.DoesNotContain(
                $"ix_{tableNames[model.Name]}_{field.Name} ", Pg(model, on: false));
        }
    }

    // ---- 켜면 덮이지 않은 FK 에 인덱스가 붙는다 ----

    [Fact]
    public void Enabled_indexes_an_uncovered_foreign_key_on_both_dialects()
    {
        // WorkOrder.requester_id 는 @reference 이지만 어떤 인덱스 선언에도 등장하지 않는다.
        var workOrder = Model.Value.Lookup["WorkOrder"];

        Assert.Contains("requester_id", Planned(workOrder));

        Assert.Contains(
            "CREATE NONCLUSTERED INDEX [IX_WorkOrder_RequesterId] ON [dbo].[WorkOrder] ([RequesterId]);",
            Tsql(workOrder, on: true));
        Assert.Contains(
            "CREATE INDEX ix_work_order_requester_id ON public.work_order (requester_id);",
            Pg(workOrder, on: true));
    }

    [Fact]
    public void A_model_without_foreign_keys_gains_nothing()
    {
        var site = Model.Value.Lookup["Site"];   // 참조를 갖지 않는다

        Assert.Empty(Planned(site));
        Assert.Equal(Tsql(site, on: false), Tsql(site, on: true));
    }

    // ---- 이미 덮인 컬럼은 건너뛴다 ----

    [Fact]
    public void A_foreign_key_the_model_already_indexes_is_not_indexed_twice()
    {
        // Building.site_id 는 @reference 이면서 @index(site_id) 로도 선언돼 있다.
        var building = Model.Value.Lookup["Building"];

        Assert.DoesNotContain("site_id", Planned(building));

        var sql = Tsql(building, on: true);
        Assert.Equal(1, sql.Split("[IX_Building_SiteId]").Length - 1);
    }

    [Fact]
    public void A_foreign_key_that_leads_a_composite_index_is_skipped()
    {
        // Asset.floor_id 는 @index(floor_id, status) 의 **선두** 컬럼이다. 복합 인덱스는
        // 선두 컬럼만으로 하는 조회도 처리하므로, floor_id 단독 인덱스는 쓰기 비용만 늘린다.
        var asset = Model.Value.Lookup["Asset"];

        Assert.DoesNotContain("floor_id", Planned(asset));
        // 같은 모델의 department_id 는 어디에도 없으므로 대상이다 — 규칙이 무차별이 아님을 보인다.
        Assert.Contains("department_id", Planned(asset));
    }

    [Fact]
    public void A_foreign_key_that_leads_a_composite_unique_is_skipped()
    {
        // PartStock.part_id 는 @unique(part_id, site_id, bin_code, lot_no) 의 선두다 —
        // 유니크 제약도 인덱스를 소유한다.
        var partStock = Model.Value.Lookup["PartStock"];

        Assert.DoesNotContain("part_id", Planned(partStock));
        Assert.DoesNotContain("site_id", Planned(partStock));   // @index(site_id) 로도 덮여 있다
    }

    [Fact]
    public void A_foreign_key_that_is_not_the_leading_column_still_gets_its_own_index()
    {
        // 반대 방향 — Approval.approver_id 는 @unique(work_order_id, approver_id) 의 **둘째**
        // 컬럼이라 그 인덱스로는 조회되지 않는다. 선두인 work_order_id 만 덮인다.
        var approval = Model.Value.Lookup["Approval"];

        var planned = Planned(approval);
        Assert.DoesNotContain("work_order_id", planned);
        Assert.Contains("approver_id", planned);
    }
}
