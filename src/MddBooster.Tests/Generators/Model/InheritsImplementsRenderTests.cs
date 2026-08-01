using System.Text.Json;
using M3L.Native;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;
using Xunit;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// Domain-neutral base-class / interface knobs (ROADMAP §5.2 T-92-2):
/// <c>@implements(FQN, ...)</c> appends verbatim fully-qualified interfaces,
/// <c>@inherits(FQN)</c> overrides the default IyuEntity base class.
/// mdd treats the argument as an opaque string, prefixing only <c>global::</c>.
/// </summary>
public class InheritsImplementsRenderTests
{
    private static ResolvedModel ModelWith(string name, params FieldAttribute[] attrs)
    {
        var source = new ModelNode
        {
            Name = name,
            Type = ModelType.Model,
            Loc = new SourceLocation { File = "test.m3l.md" },
            Attributes = [.. attrs],
        };
        return new ResolvedModel
        {
            Name = name,
            Fields = [new FieldNode
            {
                Name = "key", Type = "string", Kind = FieldKind.Stored,
                Nullable = false, Loc = new SourceLocation { File = "t.m3l.md", Line = 1 }
            }],
            Source = source,
        };
    }

    private static FieldAttribute Attr(string name, params string[] args) => new()
    {
        Name = name,
        Args = [.. args.Select(a => JsonSerializer.SerializeToElement(a))],
    };

    [Fact]
    public void Implements_Uses_Verbatim_Fqn_Not_Hardcoded_Namespace()
    {
        var model = ModelWith("UserMasterItem", Attr("implements", "Sample.Contracts.IMasterListItem"));

        var result = EntityPairRenderer.Render(model, "Sample.Entities", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains("global::Sample.Contracts.IMasterListItem", result.Write);
        // Must NOT double-prefix (the old hardcoded behavior would produce
        // global::Sample.Contracts.Sample.Contracts.IMasterListItem).
        Assert.DoesNotContain("Sample.Contracts.Sample.Contracts", result.Write);
    }

    [Fact]
    public void Implements_Foreign_Namespace_Interface_Is_Verbatim()
    {
        var model = ModelWith("Account", Attr("implements", "Sample.Contracts.IPrincipal"));

        var result = EntityPairRenderer.Render(model, "Sample.Entities", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains("global::Sample.Contracts.IPrincipal", result.Write);
        // The renderer must not substitute a namespace of its own choosing.
        Assert.DoesNotContain("Sample.Entities.IPrincipal", result.Write);
    }

    [Fact]
    public void Implements_Multiple_Interfaces_All_Appended()
    {
        var model = ModelWith("Account",
            Attr("implements", "Sample.Contracts.IPrincipal", "Ns.IAudited"));

        var result = EntityPairRenderer.Render(model, "Sample.Entities", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains("global::Sample.Contracts.IPrincipal", result.Write);
        Assert.Contains("global::Ns.IAudited", result.Write);
    }

    [Fact]
    public void No_Implements_No_Extra_Interface()
    {
        var model = ModelWith("Order");

        var result = EntityPairRenderer.Render(model, "Sample.Entities", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains(": IyuEntity, IOrder", result.Write);
        Assert.DoesNotContain("IUserMasterList", result.Write);
    }

    [Fact]
    public void Inherits_Overrides_Default_Base_Class()
    {
        var model = ModelWith("User", Attr("inherits", "Sample.Contracts.PrincipalBase"));

        var result = EntityPairRenderer.Render(model, "Sample.Entities", extBacking: EntityPairRenderer.ExtBacking.None);

        // Base class replaced; default IyuEntity no longer present as base.
        Assert.Contains("public partial class User : global::Sample.Contracts.PrincipalBase, IUser", result.Write);
        Assert.DoesNotContain(": IyuEntity,", result.Write);
    }

    [Fact]
    public void No_Inherits_Keeps_Default_IyuEntity_Base()
    {
        var model = ModelWith("Order");

        var result = EntityPairRenderer.Render(model, "Sample.Entities", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains("public partial class Order : IyuEntity, IOrder", result.Write);
    }

    [Fact]
    public void Inherits_And_Implements_Combined()
    {
        var model = ModelWith("User",
            Attr("inherits", "Sample.Contracts.PrincipalBase"),
            Attr("implements", "Sample.Contracts.IPrincipal"));

        var result = EntityPairRenderer.Render(model, "Sample.Entities", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains(
            "public partial class User : global::Sample.Contracts.PrincipalBase, IUser, global::Sample.Contracts.IPrincipal",
            result.Write);
    }

    [Fact]
    public void Inherits_Applies_To_Ext_Read_Class_Too()
    {
        var model = ModelWith("User", Attr("inherits", "Sample.Contracts.PrincipalBase"));

        var result = EntityPairRenderer.Render(model, "Sample.Entities", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains("public partial class UserExt : global::Sample.Contracts.PrincipalBase, IUser", result.Read);
    }
}
