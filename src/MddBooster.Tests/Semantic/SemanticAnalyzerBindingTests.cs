using M3L.Native;
using MddBooster.Core.Semantic;
using Xunit;

namespace MddBooster.Tests.Semantic;

public class SemanticAnalyzerBindingTests
{
    private static ResolvedModel MakeModel(string name, params FieldNode[] fields) => new()
    {
        Name = name,
        Fields = fields,
        Source = new ModelNode { Name = name, Type = ModelType.Model, Loc = new SourceLocation { File = "test.m3l.md" } }
    };

    private static FieldNode MakeField(string name, string type, BindingDef? binding = null) => new()
    {
        Name = name,
        Type = type,
        Kind = FieldKind.Stored,
        Binding = binding,
        Loc = new SourceLocation { File = "test.m3l.md", Line = 1 }
    };

    [Fact]
    public void Binding_ValidTarget_NoError()
    {
        var item = MakeModel("UserMasterItem",
            MakeField("key", "string"),
            MakeField("display", "string"));
        var order = MakeModel("Order",
            MakeField("unit", "string", new BindingDef { Entity = "UserMasterItem", Column = "key" }));

        var analyzer = new SemanticAnalyzer([order, item], []);
        var diags = analyzer.Analyze();
        Assert.Empty(diags);
    }

    [Fact]
    public void Binding_UnknownEntity_MDD010()
    {
        var order = MakeModel("Order",
            MakeField("unit", "string", new BindingDef { Entity = "NoSuchEntity", Column = "key" }));

        var analyzer = new SemanticAnalyzer([order], []);
        var diags = analyzer.Analyze();
        Assert.Single(diags);
        Assert.Equal("MDD010", diags[0].Code);
    }

    [Fact]
    public void Binding_UnknownColumn_MDD011()
    {
        var item = MakeModel("UserMasterItem",
            MakeField("key", "string"));
        var order = MakeModel("Order",
            MakeField("unit", "string", new BindingDef { Entity = "UserMasterItem", Column = "no_such_col" }));

        var analyzer = new SemanticAnalyzer([order, item], []);
        var diags = analyzer.Analyze();
        Assert.Single(diags);
        Assert.Equal("MDD011", diags[0].Code);
    }
}
