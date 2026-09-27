using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// <c>@searchable</c> reaches the generated entities as <c>[Searchable]</c> — the declaration the
/// runtime's <c>$search</c> reads to decide which string properties a term is matched against.
/// </summary>
/// <remarks>
/// Until this, the declaration was parsed and then dropped by every target, and search matched every
/// string property of the read type: a note naming another customer, or a token that happened to
/// contain the term, matched a search meant for names.
/// </remarks>
public class SearchableEmissionTests
{
    private static IReadOnlyList<ResolvedModel> Load() =>
        new InterfaceResolver(new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "searchable-fields.m3l.md"))).ResolveAll().ToList();

    private static string PropertyBlock(string source, string property)
    {
        var at = source.IndexOf($" {property} {{ get; set; }}", StringComparison.Ordinal);
        Assert.True(at >= 0, $"property {property} not emitted");
        var start = source.LastIndexOf("{ get; set; }", at - 1, StringComparison.Ordinal);
        return source[(start < 0 ? 0 : start)..at];
    }

    [Fact]
    public void Declared_fields_carry_Searchable_on_both_classes()
    {
        var pair = EntityPairRenderer.Render(Load().Single(m => m.Name == "SearchOrder"), "Test.Entities");

        foreach (var source in new[] { pair.Read, pair.Write })
        {
            Assert.Contains("[Searchable]", PropertyBlock(source, "OrderNumber"));
            Assert.Contains("[Searchable]", PropertyBlock(source, "CustomerName"));
        }
    }

    [Fact]
    public void Undeclared_fields_carry_nothing()
    {
        var pair = EntityPairRenderer.Render(Load().Single(m => m.Name == "SearchOrder"), "Test.Entities");

        Assert.DoesNotContain("[Searchable]", PropertyBlock(pair.Read, "Memo"));
        Assert.DoesNotContain("[Searchable]", PropertyBlock(pair.Read, "ShareToken"));
    }

    [Fact]
    public void A_model_declaring_nothing_carries_no_Searchable()
    {
        var pair = EntityPairRenderer.Render(Load().Single(m => m.Name == "SearchNote"), "Test.Entities");

        Assert.DoesNotContain("[Searchable]", pair.Read);
        Assert.DoesNotContain("[Searchable]", pair.Write);
    }
}
