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

    private static string Render(TsFormModuleImports? modules = null)
    {
        var models = Models(out var enums);
        var imports = new TsFormImports { GeneratedTypesBase = "../types" };
        if (modules is not null) imports = imports with { Modules = modules };

        return TsFormRenderer.RenderAll(models, enums, imports)["OrderItem"];
    }

    /// <summary>
    /// The defaults are not a style choice — they are the exact strings the
    /// generator emitted before these became settings. Changing one silently
    /// rewrites the import block of every consumer who configured nothing, so
    /// this test exists to make that change deliberate rather than incidental.
    /// </summary>
    [Fact]
    public void Defaults_are_the_strings_the_generator_emitted_before_they_were_settings()
    {
        var defaults = new TsFormModuleImports();

        Assert.Equal("@iyulab/enterprise", defaults.Layout);
        Assert.Equal("../components/ui", defaults.Controls);
        Assert.Equal("../lib/select-options", defaults.SelectOptions);
    }

    [Fact]
    public void Configuring_nothing_reproduces_the_historical_import_block()
    {
        var form = Render();

        Assert.Contains("import { FormSection, FormRow } from '@iyulab/enterprise'", form);
        Assert.Contains("} from '../components/ui'", form);
        Assert.Contains("import { enumToOptions } from '../lib/select-options'", form);
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

        // The point of the change is that the old locations stop being required.
        Assert.DoesNotContain("@iyulab/enterprise", form);
        Assert.DoesNotContain("../components/ui", form);
        Assert.DoesNotContain("../lib/select-options", form);
    }

    /// <summary>
    /// Overriding one module must not disturb the others, or a consumer who only
    /// wants to move their controls would have to restate the other two.
    /// </summary>
    [Fact]
    public void Overriding_one_module_leaves_the_others_at_their_defaults()
    {
        var form = Render(new TsFormModuleImports { Controls = "@example/controls" });

        Assert.Contains("} from '@example/controls'", form);
        Assert.Contains("import { FormSection, FormRow } from '@iyulab/enterprise'", form);
        Assert.Contains("import { enumToOptions } from '../lib/select-options'", form);
    }

    /// <summary>
    /// The generator's own output is not in this set. It is derived, so a
    /// consumer setting cannot desynchronise it from where the files landed.
    /// </summary>
    [Fact]
    public void Configuring_the_foreign_modules_does_not_move_the_generated_type_imports()
    {
        var form = Render(new TsFormModuleImports { Controls = "@example/controls" });

        Assert.Contains("from '../types/entities_gen'", form);
        Assert.Contains("from '../types/enums_gen'", form);
        Assert.Contains("from '../types/enum_labels_gen'", form);
    }
}
