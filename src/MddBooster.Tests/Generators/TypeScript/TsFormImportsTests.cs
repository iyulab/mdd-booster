using System.Runtime.CompilerServices;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.TypeScript;

namespace MddBooster.Tests.Generators.TypeScript;

/// <summary>
/// A generated form imports three modules this generator does not write. They
/// used to be literals, which meant the consumer — who may not edit the file —
/// had to build the folder layout those literals named.
/// </summary>
public sealed class TsFormImportsTests
{
    private static IReadOnlyList<ResolvedModel> Models(out IReadOnlyList<M3L.Native.EnumNode> enums)
    {
        var ast = new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "order-with-group.m3l.md"));
        enums = ast.Enums;
        return new InterfaceResolver(ast).ResolveAll();
    }

    private static string Render(TsFormModuleImports modules)
    {
        var models = Models(out var enums);
        var imports = new TsFormImports { GeneratedTypesBase = "../types", Modules = modules };

        return TsFormRenderer.RenderAll(models, enums, imports)["OrderItem"];
    }

    /// <summary>
    /// None of the three has a default. A default could only be a guess at the
    /// consumer's folder layout or at a component library this generator does not
    /// depend on; <c>required</c> makes leaving one out a compile error for a
    /// library caller, and the CLI turns it into a configuration error that names
    /// the missing key.
    /// </summary>
    [Theory]
    [InlineData(nameof(TsFormModuleImports.Layout))]
    [InlineData(nameof(TsFormModuleImports.Controls))]
    [InlineData(nameof(TsFormModuleImports.SelectOptions))]
    public void Every_foreign_module_must_be_named_by_the_caller(string property)
    {
        var member = typeof(TsFormModuleImports).GetProperty(property)!;

        Assert.True(member.IsDefined(typeof(RequiredMemberAttribute), inherit: false),
            $"{property} must stay required — a default here names a layout or package for the consumer.");
    }

    [Fact]
    public void Each_module_can_be_pointed_somewhere_else_independently()
    {
        var form = Render(new TsFormModuleImports
        {
            Layout = "@example/layout",
            Controls = "@example/controls",
            SelectOptions = "@example/enum-options",
        });

        Assert.Contains("import { FormSection, FormRow } from '@example/layout'", form);
        Assert.Contains("} from '@example/controls'", form);
        Assert.Contains("import { enumToOptions } from '@example/enum-options'", form);
    }

    /// <summary>
    /// The generator's own output is not in this set. It is derived, so a
    /// consumer setting cannot desynchronise it from where the files landed.
    /// </summary>
    [Fact]
    public void Configuring_the_foreign_modules_does_not_move_the_generated_type_imports()
    {
        var form = Render(new TsFormModuleImports
        {
            Layout = "@example/layout",
            Controls = "@example/controls",
            SelectOptions = "@example/enum-options",
        });

        Assert.Contains("from '../types/entities_gen'", form);
        Assert.Contains("from '../types/enums_gen'", form);
        Assert.Contains("from '../types/enum_labels_gen'", form);
    }
}
