using MddBooster.Core.Semantic;
using MddBooster.Tests.TestSupport;

namespace MddBooster.Tests.Semantic;

public class ExtensionRulesTests
{
    private const string Base = "## Asset\n- id: identifier @pk\n- name: string(100)\n";

    private static IReadOnlyList<SemanticDiagnostic> Analyze(params (string, string)[] files)
    {
        var (ast, models) = TwoFileModel.Load(files);
        return new SemanticAnalyzer(models, ast.Enums.ToList()).Analyze();
    }

    [Fact]
    public void Prefixed_extension_of_a_standard_model_is_clean()
    {
        var d = Analyze(("base.m3l.md", Base),
            ("insp.m3l.md", "# Prefix: insp\n\n## Asset ::extend\n- insp_grade: string(20)?\n"));
        Assert.Empty(d);
    }

    [Fact]
    public void MDD013_field_without_the_owner_prefix()
    {
        var d = Analyze(("base.m3l.md", Base),
            ("insp.m3l.md", "# Prefix: insp\n\n## Asset ::extend\n- grade: string(20)?\n- insp_ok: boolean?\n"));
        var e = Assert.Single(d);
        Assert.Equal("MDD013", e.Code);
        Assert.Equal(SemanticSeverity.Error, e.Severity);
        Assert.Contains("insp_", e.Message);
        Assert.Contains("Asset.grade", e.Message);
    }

    [Fact]
    public void Standard_file_extending_a_standard_model_needs_no_prefix()
    {
        var d = Analyze(("base.m3l.md", Base),
            ("product.m3l.md", "## Asset ::extend\n- warranty_until: date?\n"));
        Assert.Empty(d);
    }

    [Fact]
    public void Same_owner_extension_needs_no_prefix()
    {
        var d = Analyze(
            ("insp.m3l.md", "# Prefix: insp\n\n## InspRecord\n- id: identifier @pk\n"),
            ("insp2.m3l.md", "# Prefix: insp\n\n## InspRecord ::extend\n- note: text?\n"));
        Assert.Empty(d);
    }

    [Fact]
    public void MDD014_standard_file_extending_an_owned_model_once_per_source()
    {
        var d = Analyze(
            ("insp.m3l.md", "# Prefix: insp\n\n## InspRecord\n- id: identifier @pk\n"),
            ("other.m3l.md", "## InspRecord ::extend\n- a: string?\n- b: string?\n"));
        Assert.Equal("MDD014", Assert.Single(d).Code);
    }

    [Fact]
    public void MDD015_stored_extension_field_must_be_nullable_or_defaulted()
    {
        var d = Analyze(("base.m3l.md", Base),
            ("insp.m3l.md", "# Prefix: insp\n\n## Asset ::extend\n- insp_grade: string(20)\n- insp_level: integer = 0\n- insp_note: text?\n"));
        var e = Assert.Single(d);
        Assert.Equal("MDD015", e.Code);
        Assert.Contains("Asset.insp_grade", e.Message);
    }

    [Fact]
    public void Derived_extension_fields_are_exempt_from_MDD015()
    {
        var d = Analyze(("base.m3l.md", Base),
            ("insp.m3l.md", "# Prefix: insp\n\n## Asset ::extend\n- insp_label: string @computed(\"name\")\n"));
        Assert.Empty(d);
    }

    [Theory]
    [InlineData("aspect")]
    [InlineData("subtype")]
    public void MDD016_models_with_a_base_are_not_supported_yet(string kind)
    {
        var d = Analyze(("base.m3l.md", Base + $"\n## AssetProfile ::{kind}(Asset)\n- level: integer?\n"));
        var e = Assert.Single(d);
        Assert.Equal("MDD016", e.Code);
        Assert.Contains(kind, e.Message);
    }

    [Fact]
    public void MDD017_warns_when_a_model_in_a_prefixed_file_lacks_the_prefix()
    {
        var d = Analyze(("insp.m3l.md", "# Prefix: insp\n\n## Record\n- id: identifier @pk\n\n## InspOk\n- id: identifier @pk\n"));
        var w = Assert.Single(d);
        Assert.Equal("MDD017", w.Code);
        Assert.Equal(SemanticSeverity.Warning, w.Severity);
        Assert.Contains("Record", w.Message);
    }
}
