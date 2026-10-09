using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// <c>@searchable</c> on a reference key lands on the navigation that follows the key — the member the
/// runtime's <c>$search</c> follows one step into the target's text — not on the key, which is an
/// identifier and matches no term.
/// </summary>
/// <remarks>
/// Before this, the key itself carried <c>[Searchable]</c>. Being a declaration it narrowed the read
/// type's search to the declared properties, and being an identifier it contributed nothing — a model
/// that marked only its reference keys matched no search term at all.
/// </remarks>
public class SearchableReferenceEmissionTests
{
    private const string Model = """
# Namespace: test.search

## Category
- id: identifier @pk @generated
- name: string(100) @searchable

## Asset
- id: identifier @pk @generated
- title: string(200) @searchable
- category_id: identifier? @reference(Category) @searchable
- site_id: identifier? @reference(Category) @unique @searchable
- maker: identifier? @reference(Category) @searchable
- owner_id: identifier? @reference(Category)
""";

    private static IReadOnlyList<ResolvedModel> Load(string m3l)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-searchable-ref-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, m3l);
        try
        {
            return new InterfaceResolver(new M3lLoader().LoadFile(tmp)).ResolveAll().ToList();
        }
        finally { File.Delete(tmp); }
    }

    private static EntityPairRenderer.RenderedPair Render(IReadOnlyList<ResolvedModel> models, string name)
    {
        var oneToOne = OneToOneRelationships.Describe(models);
        return EntityPairRenderer.Render(
            models.Single(m => m.Name == name), "Test.Entities",
            oneToOne: oneToOne, allModels: models,
            manyToOne: ManyToOneNavigations.Describe(models, oneToOne));
    }

    /// <summary>Whether the member declared by the line containing <paramref name="declaration"/> is marked.</summary>
    private static bool Marked(string source, string declaration)
    {
        var lines = source.ReplaceLineEndings().Split(Environment.NewLine);
        var at = Array.FindIndex(lines, l => l.Contains(declaration, StringComparison.Ordinal));
        Assert.True(at >= 0, $"'{declaration}' not emitted");
        for (var i = at - 1; i >= 0 && lines[i].TrimStart().StartsWith('['); i--)
            if (lines[i].Trim() == "[Searchable]") return true;
        return false;
    }

    [Fact]
    public void A_many_to_one_key_marks_its_navigation_on_both_classes_and_not_the_key()
    {
        var pair = Render(Load(Model), "Asset");

        Assert.True(Marked(pair.Read, "public CategoryExt? Category {"));
        Assert.False(Marked(pair.Read, " CategoryId {"));
        Assert.True(Marked(pair.Write, "public Category? Category {"));
        Assert.False(Marked(pair.Write, " CategoryId {"));
    }

    [Fact]
    public void A_one_to_one_key_marks_the_forward_navigation()
    {
        var pair = Render(Load(Model), "Asset");

        Assert.True(Marked(pair.Read, "public CategoryExt? Site {"));
        Assert.False(Marked(pair.Read, " SiteId {"));
    }

    [Fact]
    public void A_key_without_a_navigation_keeps_the_declaration_and_is_reported()
    {
        var models = Load(Model);
        var pair = Render(models, "Asset");

        // `maker` does not follow the `_id` convention — neither class navigates it.
        Assert.True(Marked(pair.Read, " Maker {"));
        Assert.True(Marked(pair.Write, " Maker {"));

        var warning = Assert.Single(new SemanticAnalyzer(models, []).Analyze(), d => d.Code == "MDD034");
        Assert.Equal(SemanticSeverity.Warning, warning.Severity);
        Assert.Contains("Asset.maker", warning.Message);
    }

    [Fact]
    public void A_navigation_left_out_for_a_name_collision_keeps_the_declaration_on_the_read_key()
    {
        var models = Load("""
# Namespace: test.search

## Category
- id: identifier @pk @generated
- name: string(100)

## Asset
- id: identifier @pk @generated
- category_id: identifier? @reference(Category) @searchable
- category: string(100)
""");
        var pair = Render(models, "Asset");

        // The read type's `Category` is the declared field, so the key keeps the mark there; the write
        // type still navigates the key and carries the mark on that navigation.
        Assert.True(Marked(pair.Read, " CategoryId {"));
        Assert.True(Marked(pair.Write, "public Category? Category {"));
        Assert.False(Marked(pair.Write, " CategoryId {"));

        var warning = Assert.Single(new SemanticAnalyzer(models, []).Analyze(), d => d.Code == "MDD034");
        Assert.Contains("MDD027", warning.Message);
    }

    [Fact]
    public void Navigated_keys_and_unmarked_keys_are_not_reported()
    {
        var models = Load(Model);
        var diagnostics = new SemanticAnalyzer(models, []).Analyze().Where(d => d.Code == "MDD034").ToList();

        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("category_id", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("site_id", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("owner_id", StringComparison.Ordinal));
        Assert.False(Marked(Render(models, "Asset").Read, "public CategoryExt? Owner {"));
    }
}
