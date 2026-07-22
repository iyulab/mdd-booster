using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using Microsoft.CodeAnalysis.CSharp;

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

    /// <summary>
    /// 2026-07-22 회귀 — `@primary`(스펙상 `@pk`의 정본 표기)로 선언한 PK 필드가
    /// elide되지 않고 일반 속성으로 렌더되어 상속된 `IyuEntity.Id`와 충돌함.
    /// </summary>
    [Fact]
    public void Primary_alias_pk_field_is_elided_like_pk()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("primary-alias.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Sample");

        var rendered = EntityPairRenderer.Render(resolved, "Test.Entities");

        // PK 필드는 IyuEntity.Id 상속으로 대체 — 중복 Id 속성이 나오면 안 된다.
        Assert.DoesNotContain("public Guid Id", rendered.Write);
        Assert.Contains("public string Name { get; set; }", rendered.Write);
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

    [Fact]
    public void Decimal_with_precision_emits_Column_TypeName_attribute()
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "order-with-derived.m3l.md"));
        var enumNames = new HashSet<string>(ast.Enums.Select(e => e.Name), StringComparer.Ordinal);
        var order = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");
        var pair = EntityPairRenderer.Render(order, "Test.Orders", enumNames, EntityPairRenderer.ExtBacking.Ext);

        // subtotal: decimal(12,2) stored → [Column(TypeName = "decimal(12,2)")]
        Assert.Contains("[Column(TypeName = \"decimal(12,2)\")]", pair.Write);

        // Generated code must be valid C#
        var tree = CSharpSyntaxTree.ParseText(pair.Write);
        Assert.DoesNotContain(tree.GetDiagnostics(),
            d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }
}
