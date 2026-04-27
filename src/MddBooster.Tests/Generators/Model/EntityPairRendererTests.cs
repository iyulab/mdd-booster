using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

public class EntityPairRendererTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static ResolvedModel LoadBankAccount()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("bank-account.m3l.md"));
        return new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "BankAccount");
    }

    [Fact]
    public void Render_produces_interface_write_read_with_expected_shape()
    {
        var resolved = LoadBankAccount();
        var rendered = EntityPairRenderer.Render(resolved, "Yesung.Entities");

        // Interface — read-only contract
        Assert.Contains("public interface IBankAccount", rendered.Interface);
        Assert.Contains("global::System.Guid Id { get; }", rendered.Interface);
        Assert.Contains("string BankName { get; }", rendered.Interface);
        Assert.Contains("string AccountNumber { get; }", rendered.Interface);
        Assert.Contains("string HolderName { get; }", rendered.Interface);
        Assert.Contains("string? Note { get; }", rendered.Interface);
        Assert.Contains("bool IsActive { get; }", rendered.Interface);

        // Write class — base table
        Assert.Contains("[Table(\"BankAccount\")]", rendered.Write);
        Assert.Contains("public partial class BankAccount", rendered.Write);
        Assert.Contains(": global::Iyu.Core.Entities.IyuEntity, IBankAccount", rendered.Write);
        Assert.Contains("public string BankName { get; set; } = string.Empty;", rendered.Write);
        Assert.Contains("public string? Note { get; set; }", rendered.Write);
        Assert.Contains("public bool IsActive { get; set; }", rendered.Write);

        // Read class — BankAccount has no derived fields so the Ext read
        // model maps back to the base table (no _ext view exists).
        Assert.Contains("[Table(\"BankAccount\")]", rendered.Read);
        Assert.Contains("public partial class BankAccountExt", rendered.Read);
        Assert.Contains(": global::Iyu.Core.Entities.IyuEntity, IBankAccount", rendered.Read);
    }

    [Fact]
    public void Ext_class_points_at_underscore_ext_view_only_when_model_has_derived_fields()
    {
        var resolved = LoadBankAccount();
        var bankRendered = EntityPairRenderer.Render(resolved, "Yesung.Entities", extBacking: EntityPairRenderer.ExtBacking.None);
        // No derived fields → Ext model maps to base table
        Assert.Contains("[Table(\"BankAccount\")]", bankRendered.Read);

        var withFull = EntityPairRenderer.Render(resolved, "Yesung.Entities", extBacking: EntityPairRenderer.ExtBacking.Full);
        Assert.Contains("[Table(\"BankAccountFullView\")]", withFull.Read);

        var withExt = EntityPairRenderer.Render(resolved, "Yesung.Entities", extBacking: EntityPairRenderer.ExtBacking.Ext);
        Assert.Contains("[Table(\"BankAccountExtView\")]", withExt.Read);
    }

    [Fact]
    public void Render_elides_pk_id_and_inherited_timestamps()
    {
        var resolved = LoadBankAccount();
        var rendered = EntityPairRenderer.Render(resolved, "Yesung.Entities");

        // Id comes from IyuEntity — must NOT be redeclared on the classes.
        Assert.DoesNotContain("public global::System.Guid Id { get; set; }", rendered.Write);
        // CreatedAt/UpdatedAt also inherited — must NOT be redeclared.
        Assert.DoesNotContain("public DateTimeOffset CreatedAt { get; set; }", rendered.Write);
        Assert.DoesNotContain("public DateTimeOffset UpdatedAt { get; set; }", rendered.Write);
    }

    [Fact]
    public void Render_namespace_is_applied()
    {
        var resolved = LoadBankAccount();
        var rendered = EntityPairRenderer.Render(resolved, "Custom.Namespace");

        Assert.Contains("namespace Custom.Namespace;", rendered.Interface);
        Assert.Contains("namespace Custom.Namespace;", rendered.Write);
        Assert.Contains("namespace Custom.Namespace;", rendered.Read);
    }

    [Fact]
    public void Render_rejects_null_model()
    {
        Assert.Throws<ArgumentNullException>(() => EntityPairRenderer.Render(null!, "ns"));
    }

    [Fact]
    public void Render_rejects_empty_namespace()
    {
        var resolved = LoadBankAccount();
        Assert.Throws<ArgumentException>(() => EntityPairRenderer.Render(resolved, ""));
    }
}
