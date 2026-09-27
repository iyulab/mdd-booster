using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Api;

namespace MddBooster.Tests.Generators.Api;

/// <summary>
/// An <c>::aspect(Base)</c> set is declared to the runtime as sharing its key with the base set —
/// the fact that lets the generic write path refuse a row that refers to no base row, instead of
/// leaving that to a database constraint (or, on a schema built from the EF model, to nothing).
/// </summary>
public class AspectSharedKeyRegistrationTests
{
    private static IReadOnlyList<ResolvedModel> Load(string fixture) =>
        new InterfaceResolver(new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", fixture))).ResolveAll().ToList();

    private const string Declaration =
        "options.ODataModel.DeclareSharedKey(\"AssetMaintenanceProfiles\", \"Assets\");";

    [Fact]
    public void An_aspect_set_is_declared_to_share_its_bases_key()
    {
        var src = ApiRegistrationRenderer.Render(Load("aspect-shared-key.m3l.md"), "Test.Api");

        Assert.Contains(Declaration, src);
    }

    [Fact]
    public void The_declaration_follows_every_set_registration()
    {
        // The runtime refuses a declaration naming a set it has not registered yet, so both sets must
        // be added first — whatever order the models sort in.
        var src = ApiRegistrationRenderer.Render(Load("aspect-shared-key.m3l.md"), "Test.Api");

        var declared = src.IndexOf(Declaration, StringComparison.Ordinal);
        Assert.True(declared > src.LastIndexOf("options.ODataModel.AddEntityPair<", StringComparison.Ordinal));
        Assert.True(declared > src.LastIndexOf("options.GraphQL.AddEntityPair<", StringComparison.Ordinal));
    }

    [Fact]
    public void A_model_without_aspects_declares_nothing()
    {
        var src = ApiRegistrationRenderer.Render(Load("order-with-derived.m3l.md"), "Test.Api");

        Assert.DoesNotContain("DeclareSharedKey", src);
    }
}
