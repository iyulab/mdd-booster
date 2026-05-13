using System.Text.Json;
using M3L.Native;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using Xunit;

namespace MddBooster.Tests.Generators.Model;

public class ImplementsRenderTests
{
    [Fact]
    public void Implements_Interface_Appended_To_Class()
    {
        var source = new ModelNode
        {
            Name = "UserMasterItem",
            Type = ModelType.Model,
            Loc = new SourceLocation { File = "test.m3l.md" },
            Attributes = [
                new FieldAttribute
                {
                    Name = "implements",
                    Args = [JsonSerializer.SerializeToElement("IUserMasterList")]
                }
            ]
        };
        var model = new ResolvedModel
        {
            Name = "UserMasterItem",
            Fields = [new FieldNode { Name = "key", Type = "string", Kind = FieldKind.Stored,
                Nullable = false, Loc = new SourceLocation { File = "t.m3l.md", Line = 1 } }],
            Source = source
        };

        var result = EntityPairRenderer.Render(model, "Yesung", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains("global::Iyu.Core.Entities.IUserMasterList", result.Write);
    }

    [Fact]
    public void No_Extra_Interface_Without_Implements()
    {
        var source = new ModelNode { Name = "Order", Type = ModelType.Model, Loc = new SourceLocation { File = "test.m3l.md" } };
        var model = new ResolvedModel
        {
            Name = "Order",
            Fields = [new FieldNode { Name = "status", Type = "string", Kind = FieldKind.Stored,
                Nullable = false, Loc = new SourceLocation { File = "t.m3l.md", Line = 1 } }],
            Source = source
        };

        var result = EntityPairRenderer.Render(model, "Yesung", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.DoesNotContain("IUserMasterList", result.Write);
    }
}
