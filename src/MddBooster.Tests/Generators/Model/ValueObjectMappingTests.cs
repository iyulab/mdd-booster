using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

public class ValueObjectMappingTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Phone_email_url_fields_map_to_string()
    {
        // phone/email/url m3l types → plain C# string (not Iyu.Core.ValueObjects)
        var ast = new M3lLoader().LoadFile(FixturePath("contact-with-vos.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Contact");

        var rendered = EntityPairRenderer.Render(resolved, "Test.Contacts");

        Assert.Contains("public string PhoneNumber { get; set; } = string.Empty;", rendered.Write);
        Assert.Contains("public string EmailAddress { get; set; } = string.Empty;", rendered.Write);
        Assert.Contains("public string? Homepage { get; set; }", rendered.Write);

        Assert.Contains("string PhoneNumber { get; }", rendered.Interface);
        Assert.Contains("string? Homepage { get; }", rendered.Interface);
    }

    [Fact]
    public void Phone_email_url_map_to_plain_string_for_OData_compatibility()
    {
        // Value object types (PhoneNumber/EmailAddress/WebUrl) cause OData
        // serialization failures (connection reset) because ODataConventionModelBuilder
        // cannot register them as EDM complex types. Map to plain string instead.
        var ast = new M3lLoader().LoadFile(FixturePath("contact-with-vos.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Contact");

        var rendered = EntityPairRenderer.Render(resolved, "Test.Contacts");

        // Write + Read: plain string, not Iyu.Core.ValueObjects types
        Assert.Contains("public string PhoneNumber { get; set; } = string.Empty;", rendered.Write);
        Assert.Contains("public string EmailAddress { get; set; } = string.Empty;", rendered.Write);
        Assert.Contains("public string? Homepage { get; set; }", rendered.Write);

        Assert.Contains("public string PhoneNumber { get; set; } = string.Empty;", rendered.Read);
        Assert.Contains("public string? Homepage { get; set; }", rendered.Read);

        // Interface: same string types
        Assert.Contains("string PhoneNumber { get; }", rendered.Interface);
        Assert.Contains("string? Homepage { get; }", rendered.Interface);

        // Value object types must NOT appear anywhere in generated output
        Assert.DoesNotContain("Iyu.Core.ValueObjects", rendered.Write);
        Assert.DoesNotContain("Iyu.Core.ValueObjects", rendered.Read);
        Assert.DoesNotContain("Iyu.Core.ValueObjects", rendered.Interface);
    }
}
