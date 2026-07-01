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
        var model = ModelWith("UserMasterItem", Attr("implements", "Iyu.Core.Entities.IUserMasterList"));

        var result = EntityPairRenderer.Render(model, "Yesung", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains("global::Iyu.Core.Entities.IUserMasterList", result.Write);
        // Must NOT double-prefix (the old hardcoded behavior would produce
        // global::Iyu.Core.Entities.Iyu.Core.Entities.IUserMasterList).
        Assert.DoesNotContain("Iyu.Core.Entities.Iyu.Core.Entities", result.Write);
    }

    [Fact]
    public void Implements_Foreign_Namespace_Interface_Is_Verbatim()
    {
        var model = ModelWith("Account", Attr("implements", "Iyu.Core.Identity.IUser"));

        var result = EntityPairRenderer.Render(model, "Yesung", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains("global::Iyu.Core.Identity.IUser", result.Write);
        Assert.DoesNotContain("Iyu.Core.Entities.IUser", result.Write);
    }

    [Fact]
    public void Implements_Multiple_Interfaces_All_Appended()
    {
        var model = ModelWith("Account",
            Attr("implements", "Iyu.Core.Identity.IUser", "Ns.IAudited"));

        var result = EntityPairRenderer.Render(model, "Yesung", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains("global::Iyu.Core.Identity.IUser", result.Write);
        Assert.Contains("global::Ns.IAudited", result.Write);
    }

    [Fact]
    public void No_Implements_No_Extra_Interface()
    {
        var model = ModelWith("Order");

        var result = EntityPairRenderer.Render(model, "Yesung", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains(": global::Iyu.Core.Entities.IyuEntity, IOrder", result.Write);
        Assert.DoesNotContain("IUserMasterList", result.Write);
    }

    [Fact]
    public void Inherits_Overrides_Default_Base_Class()
    {
        var model = ModelWith("User", Attr("inherits", "Iyu.Core.Identity.IyuUser"));

        var result = EntityPairRenderer.Render(model, "Yesung", extBacking: EntityPairRenderer.ExtBacking.None);

        // Base class replaced; default IyuEntity no longer present as base.
        Assert.Contains("public partial class User : global::Iyu.Core.Identity.IyuUser, IUser", result.Write);
        Assert.DoesNotContain("Iyu.Core.Entities.IyuEntity", result.Write);
    }

    [Fact]
    public void No_Inherits_Keeps_Default_IyuEntity_Base()
    {
        var model = ModelWith("Order");

        var result = EntityPairRenderer.Render(model, "Yesung", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains("public partial class Order : global::Iyu.Core.Entities.IyuEntity, IOrder", result.Write);
    }

    [Fact]
    public void Inherits_And_Implements_Combined()
    {
        var model = ModelWith("User",
            Attr("inherits", "Iyu.Core.Identity.IyuUser"),
            Attr("implements", "Iyu.Core.Identity.IUser"));

        var result = EntityPairRenderer.Render(model, "Yesung", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains(
            "public partial class User : global::Iyu.Core.Identity.IyuUser, IUser, global::Iyu.Core.Identity.IUser",
            result.Write);
    }

    [Fact]
    public void Inherits_Applies_To_Ext_Read_Class_Too()
    {
        var model = ModelWith("User", Attr("inherits", "Iyu.Core.Identity.IyuUser"));

        var result = EntityPairRenderer.Render(model, "Yesung", extBacking: EntityPairRenderer.ExtBacking.None);

        Assert.Contains("public partial class UserExt : global::Iyu.Core.Identity.IyuUser, IUser", result.Read);
    }
}
