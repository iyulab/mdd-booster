using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// The Model target's EF mapping is one <c>IEntityTypeConfiguration&lt;T&gt;</c> per entity type, and the context only
/// applies them — by name, in a fixed order, with no mapping statement of its own.
/// </summary>
public class EntityConfigurationRendererTests
{
    // Two models declared out of name order, and two keyed views likewise — the order applied is the names', not the
    // declaration's.
    private const string Source = """
        # Namespace: test.configurations

        ## Product
        - id: identifier @pk @generated
        - name: string(100) @not_null
        - category_id: identifier @reference(Category)
        - is_active: boolean @not_null

        ## Category
        - id: identifier @pk @generated
        - title: string(50) @not_null

        ## TopProducts ::view

        ### Source
        - from: Product

        - id: identifier @from(Product.id)
        - name: string @from(Product.name)

        ## ActiveProducts ::view

        ### Source
        - from: Product
        - where: "is_active = true"

        - id: identifier @from(Product.id)
        - name: string @from(Product.name)
        """;

    private static ModelMapping Render(string source, bool postgresNaming = false)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-config-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, source);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            var models = new InterfaceResolver(ast).ResolveAll().ToList();
            var views = ViewResolver.Resolve(ast.Views, models);
            Assert.Empty(views.Diagnostics);
            return ModelMappingRenderer.Render(models, "AppDbContext", "Test.Entities",
                postgresNaming: postgresNaming, readViews: views.Views);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    private static readonly string[] AppliedOrder =
        ["Category", "CategoryExt", "Product", "ProductExt", "ActiveProducts", "TopProducts"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_context_applies_every_configuration_by_name_and_maps_nothing_itself(bool postgresNaming)
    {
        var mapping = Render(Source, postgresNaming);

        var applied = mapping.Context.ReplaceLineEndings("\n").Split('\n')
            .Where(l => l.Contains("ApplyConfiguration", StringComparison.Ordinal))
            .Select(l => l.Trim())
            .ToList();
        Assert.Equal(AppliedOrder.Select(t => $"modelBuilder.ApplyConfiguration(new {t}Configuration());"), applied);
        Assert.Contains(
            "base.OnModelCreating(modelBuilder);\n        modelBuilder.ApplyConfiguration(new CategoryConfiguration());",
            mapping.Context.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("modelBuilder.Entity<", mapping.Context);
        Assert.DoesNotContain("builder.", mapping.Context.Replace("modelBuilder.", "", StringComparison.Ordinal));

        Assert.Equal(AppliedOrder, mapping.Configurations.Select(c => c.EntityType));
        Assert.Equal(AppliedOrder.Select(t => t + "Configuration"), mapping.Configurations.Select(c => c.ClassName));
        Assert.Equal(mapping.Configurations.Select(c => c.Source).Prepend(mapping.Context), mapping.Sources);
    }

    /// <summary>
    /// A type whose mapping is empty today still gets its class: which types have one is a property of the model, so a
    /// context composing them can name each.
    /// </summary>
    [Fact]
    public void A_type_with_nothing_to_configure_still_gets_its_configuration_class()
    {
        var mapping = Render("""
            # Namespace: test.plain

            ## Note
            - id: identifier @pk @generated
            - body: string(200)
            """);

        var note = mapping["Note"].ReplaceLineEndings("\n");
        Assert.Contains("using Microsoft.EntityFrameworkCore;\n", note);
        Assert.Contains("using Microsoft.EntityFrameworkCore.Metadata.Builders;\n", note);
        Assert.Contains("namespace Test.Entities;\n", note);
        Assert.Contains(
            "public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>\n{\n"
            + "    public void Configure(EntityTypeBuilder<Note> builder)\n    {\n    }\n}\n",
            note);

        // The read type has its view mapping, in a class of the same shape.
        var noteExt = mapping["NoteExt"].ReplaceLineEndings("\n");
        Assert.Contains("public sealed class NoteExtConfiguration : IEntityTypeConfiguration<NoteExt>\n", noteExt);
        Assert.Contains("    public void Configure(EntityTypeBuilder<NoteExt> builder)\n", noteExt);
        Assert.Contains("        builder.ToTable((string?)null).ToView(\"Note\");\n", noteExt);
    }

    [Fact]
    public void Every_statement_is_on_the_builder_of_its_own_type()
    {
        var mapping = Render(Source);

        foreach (var configuration in mapping.Configurations)
        {
            var body = configuration.Source.ReplaceLineEndings("\n").Split('\n')
                .SkipWhile(l => !l.Contains("public void Configure(", StringComparison.Ordinal))
                .Skip(2)
                .TakeWhile(l => l != "    }");
            Assert.All(body, l => Assert.StartsWith("        builder.", l, StringComparison.Ordinal));
        }
        Assert.Contains(
            "builder.HasOne(e => e.Category).WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.NoAction);",
            mapping["Product"]);
        Assert.Contains("builder.ToView(\"ActiveProducts\");", mapping["ActiveProducts"]);
        Assert.Contains("builder.HasKey(x => x.Id);", mapping["ActiveProducts"]);
    }

    [Fact]
    public void A_type_outside_the_model_set_has_no_configuration()
    {
        var mapping = Render(Source);

        Assert.Throws<KeyNotFoundException>(() => mapping["Order"]);
    }
}
