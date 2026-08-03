using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators;

/// <summary>
/// What the two targets say about the same foreign key. One declaration reaches both, so
/// whether its column is indexed should not depend on which output you read.
/// </summary>
/// <remarks>
/// The Model target emits a reference navigation for every <c>@reference</c> field named
/// <c>xxx_id</c> — deliberately, so Entity Framework can infer insert order. That
/// navigation is also what makes EF discover a relationship, and EF indexes the properties
/// of a foreign key by convention
/// (<see href="https://learn.microsoft.com/ef/core/modeling/relationships/conventions#indexes"/>:
/// <c>ForeignKeyIndexConvention</c>, which skips keys already covered by an existing index
/// or key — the same leading-column rule <see cref="ForeignKeyIndexPlanner"/> applies).
/// <para>
/// The convention is cited here rather than simulated. Building a real EF model would mean
/// compiling the emitted entity, which names types this project must not reference, or
/// reducing it to a shape this test authored — either way the subject would stop being the
/// generator's output. Both sets below are read from what the generators actually emit.
/// </para>
/// <para>
/// The Sql target has no such convention: it indexes a foreign key only when
/// <c>emitForeignKeyIndexes</c> is on. So while the option is off the two targets disagree,
/// and the disagreement is not a spread of edge cases — it is exactly the planner's output.
/// Turning the option on removes it. That correspondence is the subject here; whether the
/// default should change is not decided by a test.
/// </para>
/// </remarks>
public class ForeignKeyIndexCrossTargetTests
{
    private static readonly Lazy<(IReadOnlyList<ResolvedModel> Models,
                                  IReadOnlyDictionary<string, EnumNode> Enums,
                                  HashSet<string> EnumNames)> Fixture = new(() =>
    {
        var ast = new M3lLoader().LoadFile(AcceptanceModel.FixturePath);
        return (new InterfaceResolver(ast).ResolveAll().ToList(),
                ast.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal),
                new HashSet<string>(ast.Enums.Select(e => e.Name), StringComparer.Ordinal));
    });

    /// <summary>
    /// Foreign-key fields the Model target gave a navigation property, read from the
    /// emitted text rather than from the condition that emits it — a rule that stopped
    /// emitting would otherwise still agree with itself.
    /// </summary>
    private static List<FieldNode> NavigationBackedForeignKeys(ResolvedModel model)
    {
        var write = EntityPairRenderer.Render(model, "Probe", Fixture.Value.EnumNames).Write;

        return model.Fields
            .Where(f => f.Kind == FieldKind.Stored)
            .Where(f => f.Name.EndsWith("_id", StringComparison.OrdinalIgnoreCase))
            .Where(f => !string.IsNullOrEmpty(FieldAttributes.FirstArg(f, "reference")))
            .Where(f =>
            {
                var target = FieldAttributes.FirstArg(f, "reference");
                var nav = NameCasing.ToPascalCase(f.Name[..^3]);
                return write.Contains($"public {target}? {nav} ", StringComparison.Ordinal)
                    || write.Contains($"public {target} {nav} ", StringComparison.Ordinal);
            })
            .ToList();
    }

    private static string Tsql(ResolvedModel model, bool on) =>
        TableRenderer.Render(model, "dbo", Fixture.Value.Enums, false, emitForeignKeyIndexes: on);

    // ---- 기본값에서 두 타깃은 어긋난다 ----

    [Fact]
    public void With_the_option_off_the_two_targets_disagree_on_the_same_foreign_key()
    {
        // 이 단정이 재는 것은 «인덱스가 없다» 가 아니라 «두 산출물이 서로 다른 답을 준다» 이다.
        // 어긋남의 범위가 planner 의 출력과 정확히 같다는 것까지 걸어 둔다 — 산발적인 예외가
        // 아니라 한 규칙의 부재라는 뜻이고, ③(기본값) 판단이 그 사실 위에 선다.
        var disagreeing = new List<string>();
        var navBacked = 0;

        foreach (var model in Fixture.Value.Models)
        {
            var nav = NavigationBackedForeignKeys(model);
            navBacked += nav.Count;

            var planned = new HashSet<string>(
                ForeignKeyIndexPlanner.Plan(model).Select(f => f.Name), StringComparer.Ordinal);
            var sql = Tsql(model, on: false);

            foreach (var f in nav.Where(f => planned.Contains(f.Name)))
            {
                var column = NameCasing.ToPascalCase(f.Name);
                Assert.DoesNotContain($"[IX_{model.Name}_{column}]", sql);
                disagreeing.Add($"{model.Name}.{f.Name}");
            }
        }

        // 픽스처가 이 단정을 지탱하는지 — 내비게이션이 하나도 없으면 위 루프는 공허하다.
        Assert.True(navBacked > 0, "모델이 내비게이션을 가진 FK 를 하나도 내지 않는다");
        Assert.NotEmpty(disagreeing);
    }

    // ---- 옵션을 켜면 어긋남이 사라진다 ----

    [Fact]
    public void Turning_the_option_on_closes_the_gap_for_every_navigation_backed_foreign_key()
    {
        foreach (var model in Fixture.Value.Models)
        {
            var sql = Tsql(model, on: true);
            var declared = new HashSet<string>(
                ForeignKeyIndexPlanner.Plan(model).Select(f => f.Name), StringComparer.Ordinal);

            foreach (var f in NavigationBackedForeignKeys(model).Where(f => declared.Contains(f.Name)))
            {
                var column = NameCasing.ToPascalCase(f.Name);
                Assert.Contains($"[IX_{model.Name}_{column}]", sql);
            }
        }
    }

    // ---- 규칙은 무차별이 아니다 ----

    [Fact]
    public void A_foreign_key_without_a_navigation_is_not_counted_as_a_disagreement()
    {
        // `AssetSpec.asset_id` 는 공유 PK 확장의 FK 다. Model 타깃은 내비게이션을 내지 않고,
        // Sql 타깃도 PK 가 인덱스를 소유하므로 아무것도 내지 않는다 — 두 타깃이 **합의**한다.
        // 이런 자리가 없으면 위 두 단정은 "모든 FK 가 어긋난다"와 구별되지 않는다.
        var spec = Fixture.Value.Models.Single(m => m.Name == "AssetSpec");

        Assert.Empty(NavigationBackedForeignKeys(spec));
        Assert.DoesNotContain("asset_id", ForeignKeyIndexPlanner.Plan(spec).Select(f => f.Name));
    }
}
