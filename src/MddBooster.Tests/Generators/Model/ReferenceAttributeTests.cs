using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

public class ReferenceAttributeTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Render_emits_reference_attribute_on_fk_property()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-ref.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var rendered = EntityPairRenderer.Render(resolved, "Test.Yesung");

        Assert.Contains("[global::Iyu.Core.Attributes.Reference(\"Customer\")]", rendered.Write);
        Assert.Contains("public Guid CustomerId", rendered.Write);
    }
}
