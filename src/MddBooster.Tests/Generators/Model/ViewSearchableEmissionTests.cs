using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// <c>@searchable</c> on a column of a view served as a read model reaches the read type as <c>[Searchable]</c> — the
/// same declaration, with the same meaning, as on a model's field (<see cref="SearchableEmissionTests"/>).
/// </summary>
/// <remarks>
/// The runtime's <c>$search</c> reads the attribute from the read type: once a type marks any property only the marked
/// ones are searched, and none marked means every string property is. A view whose declaration was dropped searched all
/// of its string columns whatever its author wrote.
/// </remarks>
public class ViewSearchableEmissionTests
{
    private const string Source = """
        # Namespace: test

        ## UnitOfMeasure
        - id: identifier @pk @generated
        - code: string(20) @not_null
        - name: string(100) @not_null
        - legal_basis: string(400)

        ## UnitOfMeasureLookup ::view
        ### Source
        - from: UnitOfMeasure

        - id: identifier @from(UnitOfMeasure.id)
        - code: string @from(UnitOfMeasure.code) @searchable "key"
        - name: string @from(UnitOfMeasure.name) @searchable
        - legal_basis: string? @from(UnitOfMeasure.legal_basis) "basis"

        ## UnitOfMeasureNotes ::view
        ### Source
        - from: UnitOfMeasure

        - id: identifier @from(UnitOfMeasure.id)
        - note: string? @from(UnitOfMeasure.legal_basis)
        """;

    private static string Render(string viewName)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-view-search-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, Source);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var view = ViewResolver.Resolve(ast.Views, new InterfaceResolver(ast).ResolveAll())
                .Views.Single(v => v.Name == viewName);
            return ViewEntityRenderer.Render(view, "Test.Entities", knownEnumNames: null);
        }
        finally { File.Delete(tmp); }
    }

    private static string PropertyBlock(string source, string property)
    {
        var at = source.IndexOf($" {property} {{ get; set; }}", StringComparison.Ordinal);
        Assert.True(at >= 0, $"property {property} not emitted");
        var start = source.LastIndexOf("{ get; set; }", at - 1, StringComparison.Ordinal);
        return source[(start < 0 ? 0 : start)..at];
    }

    [Fact]
    public void Declared_columns_carry_Searchable_on_the_read_type()
    {
        var source = Render("UnitOfMeasureLookup");

        Assert.Contains("[Searchable]", PropertyBlock(source, "Code"));
        Assert.Contains("[Searchable]", PropertyBlock(source, "Name"));
    }

    [Fact]
    public void Undeclared_columns_carry_nothing()
    {
        var source = Render("UnitOfMeasureLookup");

        Assert.DoesNotContain("[Searchable]", PropertyBlock(source, "LegalBasis"));
        Assert.DoesNotContain("[Searchable]", PropertyBlock(source, "Id"));
    }

    [Fact]
    public void A_view_declaring_nothing_carries_no_Searchable()
    {
        Assert.DoesNotContain("[Searchable]", Render("UnitOfMeasureNotes"));
    }
}
