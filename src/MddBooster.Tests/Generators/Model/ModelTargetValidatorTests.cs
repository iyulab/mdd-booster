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

        var ex = Assert.Throws<InvalidOperationException>(
            () => ModelTargetValidator.Validate(models));

        Assert.Contains("created_at", ex.Message);
        Assert.Contains("Timestampable", ex.Message);
    }
}
