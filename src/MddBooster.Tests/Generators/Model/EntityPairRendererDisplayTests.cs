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
}
