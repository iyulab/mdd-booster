using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using MddBooster.Generators.TypeScript;

namespace MddBooster.Tests.Generators;

/// <summary>
/// The specification gives a field, a model and an enum value a display label of its own — <c>name(Label)</c>, in
/// parentheses after the name — separate from the description. A target that names the thing to a person reads the
/// label; the description is what is shown when no label is written.
/// </summary>
/// <remarks>
/// Before this, the label was parsed and dropped: every target took the description, so a field written
/// <c>- name(이름): string "…"</c> with an indented blockquote under it was labelled with the blockquote's paragraph —
/// the blockquote replaces the inline description (§4.2.6), and the label it was meant to sit beside went nowhere.
/// </remarks>
public class DisplayLabelTests
{
    private static M3lAst Load(string body)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-label-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + body);
        try { return new M3lLoader().LoadFile(tmp); }
        finally { File.Delete(tmp); }
    }

    private const string FieldWithLabelAndBlockquote =
        "## Customer\n" +
        "- id: identifier @pk @generated\n" +
        "- name(이름): string(100) \"고객의 법적 이름\"\n" +
        "  > 사업자 등록증에 적힌 이름을 쓴다.\n" +
        "  > 두 줄째 설명.\n";

    [Fact]
    public void A_field_label_is_the_display_name_and_the_blockquote_stays_the_description()
    {
        var models = new InterfaceResolver(Load(FieldWithLabelAndBlockquote)).ResolveAll();
        var write = EntityPairRenderer.Render(models[0], "Test.Ns").Write;
        Assert.Contains("[Display(Name = \"이름\")]", write);
        Assert.DoesNotContain("Name = \"사업자", write);

        var schema = TsFieldSchemaRenderer.RenderAll(models);
        Assert.Contains("label: '이름'", schema);
        Assert.DoesNotContain("label: '사업자", schema);
    }

    [Fact]
    public void A_field_without_a_label_keeps_its_description_as_the_display_name()
    {
        var models = new InterfaceResolver(Load(
            "## Item\n- id: identifier @pk @generated\n- name: string(50) \"품목명\"\n")).ResolveAll();
        Assert.Contains("[Display(Name = \"품목명\")]", EntityPairRenderer.Render(models[0], "Test.Ns").Write);
    }

    [Fact]
    public void A_lookup_without_a_label_borrows_the_label_of_the_field_it_reads()
    {
        var models = new InterfaceResolver(Load(
            "## Customer\n- id: identifier @pk @generated\n- name(고객명): string(100) \"긴 설명\"\n\n" +
            "## Order\n- id: identifier @pk @generated\n- customer_id: identifier @reference(Customer)\n" +
            "- customer_name: string @lookup(customer_id.name)\n")).ResolveAll();
        var schema = TsFieldSchemaRenderer.RenderAll(models);
        Assert.Contains("CustomerName: { label: '고객명', derived: 'lookup'", schema);
    }

    [Fact]
    public void The_legacy_label_attribute_is_still_read_and_the_build_says_to_move_it()
    {
        var models = new InterfaceResolver(Load(
            "## Sample\n- id: identifier @pk @generated\n- notes: text? @label(\"Notes\")\n")).ResolveAll();
        Assert.Contains("[Display(Name = \"Notes\")]", EntityPairRenderer.Render(models[0], "Test.Ns").Write);

        var warning = Assert.Single(new SemanticAnalyzer(models, []).Analyze(), d => d.Code == "MDD047");
        Assert.Equal(SemanticSeverity.Warning, warning.Severity);
        Assert.Contains("- notes(Notes)", warning.Message);
    }

    [Fact]
    public void The_label_in_parentheses_wins_over_the_legacy_attribute_and_the_warning_names_the_loser()
    {
        var models = new InterfaceResolver(Load(
            "## Sample\n- id: identifier @pk @generated\n- notes(메모): text? @label(\"Notes\")\n")).ResolveAll();
        Assert.Contains("[Display(Name = \"메모\")]", EntityPairRenderer.Render(models[0], "Test.Ns").Write);
        var warning = Assert.Single(new SemanticAnalyzer(models, []).Analyze(), d => d.Code == "MDD047");
        Assert.Contains("'Notes' 는 쓰이지 않는다", warning.Message);
    }

    private const string EnumWithLabels =
        "## Status ::enum\n" +
        "- open(Opened): \"Waiting for someone to pick it up\"\n" +
        "- closed: \"Done\"\n" +
        "- archived\n";

    [Fact]
    public void An_enum_value_label_is_its_display_name_and_the_description_is_the_fallback()
    {
        var status = Load(EnumWithLabels).Enums.Single();

        var csharp = EnumRenderer.Render(status, "Test.Ns");
        Assert.Contains("[Display(Name = \"Opened\")]", csharp);
        Assert.Contains("[Display(Name = \"Done\")]", csharp);
        Assert.DoesNotContain("Name = \"Waiting", csharp);
        // The description is still the member's doc comment.
        Assert.Contains("Waiting for someone to pick it up", csharp);

        var labels = TsEnumLabelsRenderer.RenderAll([status]);
        Assert.Contains("open: 'Opened'", labels);
        Assert.Contains("closed: 'Done'", labels);
        Assert.Contains("archived: 'Archived'", labels);
    }

    [Fact]
    public void A_header_label_no_target_reads_is_reported_unconsumed()
    {
        var unconsumed = AstAccounting.ListUnconsumed(Load(
            "## Customer(고객)\n- id: identifier @pk @generated\n\n## Status(상태) ::enum\n- open\n"));
        Assert.Contains("Customer: 머리 라벨 (고객)", unconsumed);
        Assert.Contains("Status ::enum: 머리 라벨 (상태)", unconsumed);
    }
}
