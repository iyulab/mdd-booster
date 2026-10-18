using MddBooster.Core.Generation;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// 2026-07-22 — 타임스탬프 계약 게이트. `IyuEntity`는 CreatedAt/UpdatedAt을 항상
/// 매핑하는데 SQL 타깃은 선언 필드만 렌더하므로, 미선언 모델은 생성 성공 후
/// 런타임에 파탄난다(silent). 생성 시점의 명시적 오류로 승격한다.
/// </summary>
public class ModelTargetValidatorTests
{
    private static IReadOnlyList<ResolvedModel> Load(string name) =>
        new InterfaceResolver(new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", name))).ResolveAll().ToList();

    [Fact]
    public void Passes_when_timestamps_come_from_interface_inheritance()
    {
        // bank-account: Timestampable ::interface 상속 — resolved 필드에 타임스탬프 포함.
        ModelTargetValidator.Validate(Load("bank-account.m3l.md"));
    }

    [Fact]
    public void Fails_loudly_when_model_lacks_timestamps()
    {
        var models = Load("order-with-nullable-ref.m3l.md");

        var ex = Assert.Throws<GeneratorRefusalException>(
            () => ModelTargetValidator.Validate(models));

        Assert.Contains("created_at", ex.Message);
        Assert.Contains("Timestampable", ex.Message);
    }

    /// <summary>
    /// A model named like another model's read type or configuration class would generate two classes of one name in
    /// one folder. The target refuses before writing, naming both owners.
    /// </summary>
    [Theory]
    [InlineData("OrderExt", "'OrderExt' — model 'Order' · model 'OrderExt'")]
    [InlineData("OrderConfiguration", "'OrderConfiguration' — model 'Order' · model 'OrderConfiguration'")]
    public void Refuses_two_generated_types_of_one_name(string second, string clash)
    {
        var models = LoadSource(
            "## Order : Timestampable\n- id: identifier @pk @generated\n\n"
            + $"## {second} : Timestampable\n- id: identifier @pk @generated\n");

        var ex = Assert.Throws<GeneratorRefusalException>(() => ModelTargetValidator.Validate(models));

        Assert.Contains(clash, ex.Message);
    }

    [Fact]
    public void Refuses_a_context_named_like_a_generated_type()
    {
        var models = LoadSource("## Order : Timestampable\n- id: identifier @pk @generated\n");

        var ex = Assert.Throws<GeneratorRefusalException>(
            () => ModelTargetValidator.Validate(models, contextName: "OrderExt"));

        Assert.Contains("'OrderExt' — model 'Order' · dbContextName", ex.Message);
    }

    [Fact]
    public void Distinct_names_pass()
    {
        ModelTargetValidator.Validate(
            LoadSource("## Order : Timestampable\n- id: identifier @pk @generated\n\n## OrderLine : Timestampable\n- id: identifier @pk @generated\n"),
            contextName: "ShopDbContext");
    }

    private static IReadOnlyList<ResolvedModel> LoadSource(string models)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mdd-validator-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(path,
            "# Namespace: test\n\n## Timestampable ::interface\n- created_at: timestamp @not_null\n- updated_at: timestamp @not_null\n\n" + models);
        try
        {
            return new InterfaceResolver(new M3lLoader().LoadFile(path)).ResolveAll().ToList();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
