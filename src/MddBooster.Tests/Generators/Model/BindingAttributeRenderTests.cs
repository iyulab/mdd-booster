using M3L.Native;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using Xunit;

namespace MddBooster.Tests.Generators.Model;

public class BindingAttributeRenderTests
{
    private static ResolvedModel MakeModel(string name, params FieldNode[] fields) => new()
    {
        Name = name,
        Fields = fields,
        Source = new ModelNode { Name = name, Type = ModelType.Model, Loc = new SourceLocation { File = "test.m3l.md" } }
    };

    [Fact]
    public void Renders_Binding_Attribute_On_Field()
    {
        var model = MakeModel("Order", new FieldNode
        {
            Name = "unit",
            Type = "string",
            Nullable = true,
            Kind = FieldKind.Stored,
            Binding = new BindingDef { Entity = "UserMasterItem", Column = "Key", IsHard = false },
            Loc = new SourceLocation { File = "test.m3l.md", Line = 1 }
        });

        var result = EntityPairRenderer.Render(model, "Yesung", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains("[global::Iyu.Core.Attributes.Binding(\"UserMasterItem\", \"Key\")]", result.Write);
    }

    [Fact]
    public void No_Binding_Attribute_When_Binding_Null()
    {
        var model = MakeModel("Order", new FieldNode
        {
            Name = "status",
            Type = "string",
            Nullable = false,
            Kind = FieldKind.Stored,
            Binding = null,
            Loc = new SourceLocation { File = "test.m3l.md", Line = 1 }
        });

        var result = EntityPairRenderer.Render(model, "Yesung", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.DoesNotContain("Binding(", result.Write);
    }
}
