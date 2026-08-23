using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

public class EntityPairRendererDisplayTests
{
    private static (IReadOnlyList<ResolvedModel> models, IReadOnlySet<string> enumNames) LoadFixture(string name)
    {
        var ast = new M3lLoader().LoadFile(Path.Combine(AppContext.BaseDirectory, "fixtures", name));
        var models = new InterfaceResolver(ast).ResolveAll();
        var enumNames = new HashSet<string>(ast.Enums.Select(e => e.Name), StringComparer.Ordinal);
        return (models, enumNames);
    }

    private static IReadOnlyList<ResolvedModel> LoadInline(string body)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-eprd-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + body);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            return new InterfaceResolver(ast).ResolveAll();
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Emits_Display_Name_when_field_has_label()
    {
        var (models, enumNames) = LoadFixture("order-with-group.m3l.md");
        var result = EntityPairRenderer.Render(models[0], "Test.Ns", enumNames);

        // name: string(50) @not_null @group("기본") "품목명"
        Assert.Contains("[Display(Name = \"품목명\"", result.Write);
    }

    [Fact]
    public void Emits_Display_GroupName_when_field_has_group()
    {
        var (models, enumNames) = LoadFixture("order-with-group.m3l.md");
        var result = EntityPairRenderer.Render(models[0], "Test.Ns", enumNames);

        Assert.Contains("GroupName = \"기본\"", result.Write);
    }

    [Fact]
    public void No_Display_when_field_has_no_label_or_group()
    {
        // item-with-constraints.m3l.md fields have no label/group
        var (models, enumNames) = LoadFixture("item-with-constraints.m3l.md");
        var result = EntityPairRenderer.Render(models[0], "Test.Ns", enumNames);

        Assert.DoesNotContain("[Display(", result.Write);
    }

    [Fact]
    public void Explicit_label_attribute_overrides_the_description()
    {
        var models = LoadInline(
            "## Sample\n" +
            "- id: identifier @pk @generated\n" +
            "- password_hash: string(64) @not_null @label(\"Password\") \"Salted hash of the user's password\"\n");
        var result = EntityPairRenderer.Render(models[0], "Test.Ns");

        Assert.Contains("[Display(Name = \"Password\"", result.Write);
        Assert.DoesNotContain("Salted hash", result.Write);
    }

    [Fact]
    public void Explicit_label_attribute_alone_is_enough_without_a_description()
    {
        var models = LoadInline(
            "## Sample\n" +
            "- id: identifier @pk @generated\n" +
            "- notes: text? @label(\"Notes\")\n");
        var result = EntityPairRenderer.Render(models[0], "Test.Ns");

        Assert.Contains("[Display(Name = \"Notes\"", result.Write);
    }
}
