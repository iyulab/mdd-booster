using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

public class ValueObjectMappingTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Phone_email_url_fields_map_to_Iyu_Core_value_objects()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("contact-with-vos.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Contact");

        var rendered = EntityPairRenderer.Render(resolved, "Test.Contacts");

        // Write class uses the fully-qualified Iyu.Core.ValueObjects types
        Assert.Contains("public global::Iyu.Core.ValueObjects.PhoneNumber PhoneNumber { get; set; }", rendered.Write);
        Assert.Contains("public global::Iyu.Core.ValueObjects.EmailAddress EmailAddress { get; set; }", rendered.Write);
        // Nullable VO — url is declared nullable
        Assert.Contains("public global::Iyu.Core.ValueObjects.WebUrl? Homepage { get; set; }", rendered.Write);

        // Interface mirrors the same types (get-only)
        Assert.Contains("global::Iyu.Core.ValueObjects.PhoneNumber PhoneNumber { get; }", rendered.Interface);
        Assert.Contains("global::Iyu.Core.ValueObjects.EmailAddress EmailAddress { get; }", rendered.Interface);
        Assert.Contains("global::Iyu.Core.ValueObjects.WebUrl? Homepage { get; }", rendered.Interface);
    }

    [Fact]
    public void VO_fields_have_no_default_initializer()
    {
        // Value objects are struct-like; non-nullable fields should get a
        // bare `;` suffix rather than `= string.Empty;`. Verify by asserting
        // the string.Empty pattern is not applied to VO properties.
        var ast = new M3lLoader().LoadFile(FixturePath("contact-with-vos.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Contact");
        var rendered = EntityPairRenderer.Render(resolved, "Test.Contacts");

        Assert.DoesNotContain("PhoneNumber PhoneNumber { get; set; } = string.Empty", rendered.Write);
        Assert.DoesNotContain("EmailAddress EmailAddress { get; set; } = string.Empty", rendered.Write);
    }
}
