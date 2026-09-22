using MddBooster.Core.Semantic;
using MddBooster.Tests.TestSupport;

namespace MddBooster.Tests.Semantic;

/// <summary>
/// <c>::aspect</c> 선언이 그 자리에서 거절되는 경우들.
/// </summary>
/// <remarks>
/// <para>
/// 이 시점의 <c>MDD016</c> 은 아직 base 를 가진 모델 전부를 거부하므로, 아래 단언은 «그 코드
/// 하나뿐인가»가 아니라 «이 코드가 함께 나오는가»를 본다. 생성이 열리는 사이클이
/// <c>MDD016</c> 을 kind 로 좁히면 그때 이 규칙들이 실제로 사용자에게 닿는다 — 규칙을 먼저
/// 세우고 문을 나중에 여는 순서이고, 반대로 하면 규칙 없는 aspect 가 생성 경로에 들어간다.
/// </para>
/// <para>
/// 🔴 여기에 <b>없는</b> 시험들이 있다. base 가 실재하지 않는 경우와 aspect 의 aspect 는 M3L 이
/// 파싱 단계에서 이미 거절하므로(<c>M3L-E017</c>·<c>E018</c>) 이 분석기에 닿지 않는다 —
/// 처음에 그 둘을 여기 썼다가 모델이 로드조차 되지 않는 것을 보고 걷어냈다. 같은 판정을 두
/// 곳에서 하면 어느 한쪽만 바뀌는 날 어긋나고, 그것을 말해 주는 것이 없다.
/// </para>
/// </remarks>
public class AspectRulesTests
{
    private const string Base = "## Asset\n- id: identifier @pk\n- name: string(100)\n";

    private static IReadOnlyList<SemanticDiagnostic> Analyze(params (string, string)[] files)
    {
        var (ast, models) = TwoFileModel.Load(files);
        return new SemanticAnalyzer(models, ast.Enums.ToList()).Analyze();
    }

    private static IEnumerable<SemanticDiagnostic> Aspect(IReadOnlyList<SemanticDiagnostic> all)
        => all.Where(d => d.Code is "MDD019" or "MDD020");

    /// <summary>
    /// 약속대로 쓴 aspect 는 aspect 규칙을 하나도 건드리지 않는다. 아래가 전부 거절이라,
    /// 이것이 없으면 「전부 거절한다」로도 통과한다.
    /// </summary>
    [Fact]
    public void A_conventional_aspect_raises_no_aspect_diagnostic()
    {
        var d = Analyze(("m.m3l.md", Base + "\n## AssetMaintenanceProfile ::aspect(Asset)\n- level: integer?\n"));

        Assert.Empty(Aspect(d));
    }




    /// <summary>
    /// 이름 약속은 경고다 — 모델이 여전히 성립하고, 오류로 막으면 기존 테이블의 개명을 강요한다.
    /// </summary>
    [Fact]
    public void MDD019_a_name_that_does_not_follow_the_convention_is_only_a_warning()
    {
        var d = Analyze(("m.m3l.md", Base + "\n## Contractor ::aspect(Asset)\n- level: integer?\n"));

        var e = Assert.Single(Aspect(d), x => x.Code == "MDD019");
        Assert.Equal(SemanticSeverity.Warning, e.Severity);
        Assert.Contains("Contractor", e.Message);
    }

    [Fact]
    public void MDD020_two_aspects_of_one_base_that_produce_the_same_navigation_name()
    {
        var d = Analyze(
            ("base.m3l.md", Base + "\n## AssetProfile ::aspect(Asset)\n- level: integer?\n"),
            ("insp.m3l.md", "# Prefix: insp\n\n## Profile ::aspect(Asset)\n- insp_note: string(50)?\n"));

        Assert.Contains(Aspect(d), x => x.Code == "MDD020");
    }

    [Fact]
    public void MDD020_a_navigation_name_that_collides_with_an_existing_field_of_the_base()
    {
        var d = Analyze(("m.m3l.md", Base + "\n## Assetname ::aspect(Asset)\n- level: integer?\n"));

        var e = Assert.Single(Aspect(d), x => x.Code == "MDD020");
        Assert.Contains("name", e.Message);
    }

    /// <summary>
    /// 약속에 맞는 이름은 <c>&lt;Base&gt;</c> 를 뗀 나머지가 되고, prefix 는 보존된다.
    /// 맞지 않으면 모델명 전체다 — 줄일 근거가 없는 이름을 줄이면 서로 다른 모델이 같은
    /// navigation 으로 무너진다.
    /// </summary>
    [Theory]
    [InlineData("AssetMaintenanceProfile", "", "MaintenanceProfile", true)]
    [InlineData("FsaAssetFacilityProfile", "fsa", "FsaFacilityProfile", true)]
    [InlineData("FsaFacilityProfile", "fsa", "FsaFacilityProfile", false)]
    [InlineData("Contractor", "", "Contractor", false)]
    public void The_reverse_navigation_name_is_derived_from_the_model_name(
        string modelName, string prefix, string expected, bool conventional)
    {
        var header = prefix.Length == 0 ? "" : $"# Prefix: {prefix}\n\n";
        var (_, models) = TwoFileModel.Load(
            ("base.m3l.md", Base),
            ("m.m3l.md", header + $"## {modelName} ::aspect(Asset)\n- level: integer?\n"));

        var model = models.Single(m => m.Name == modelName);
        var (name, matches) = AspectNaming.ReverseNavigation(model, "Asset");

        Assert.Equal(expected, name);
        Assert.Equal(conventional, matches);
    }
}
