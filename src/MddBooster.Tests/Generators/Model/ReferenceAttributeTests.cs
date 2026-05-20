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

    [Fact]
    public void Reference_field_generates_nav_property_on_write_entity()
    {
        // EF Core needs navigation properties to infer INSERT order for parent/child
        // pairs added in the same SaveChanges call. Without nav props, FK violations
        // occur when the child is inserted before the parent.
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-ref.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var rendered = EntityPairRenderer.Render(resolved, "Test.Yesung");

        // Nav property on Write class: type = reference target, name = field without _id
        Assert.Contains("public Customer Customer { get; set; } = null!;", rendered.Write);
        // FK property still present alongside nav
        Assert.Contains("public Guid CustomerId { get; set; }", rendered.Write);
        // Nav property NOT on Ext (read-only, no writes)
        Assert.DoesNotContain("public Customer Customer", rendered.Read);
        // Nav property NOT in Interface (interface is for stored field contract only)
        Assert.DoesNotContain("Customer Customer", rendered.Interface);
    }

    [Fact]
    public void Nullable_reference_field_generates_nullable_nav_property()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-nullable-ref.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var rendered = EntityPairRenderer.Render(resolved, "Test.Yesung");

        // Nullable FK → nullable nav property, no null! initializer
        Assert.Contains("public Customer? Customer { get; set; }", rendered.Write);
        Assert.DoesNotContain("public Customer? Customer { get; set; } = null!", rendered.Write);
    }
}
