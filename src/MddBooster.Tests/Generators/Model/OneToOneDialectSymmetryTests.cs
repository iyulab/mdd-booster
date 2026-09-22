using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

/// <summary>
/// The one-to-one navigation a read type gains is the same in both dialects, driven through the
/// generator rather than argued from a renderer signature.
/// </summary>
/// <remarks>
/// <para>
/// Postgres maps a read type to a snake_case view and bakes an explicit column mapping for every
/// stored column, so "does the navigation come out differently there" is the question this phase
/// has to answer before the pair is published. It is asked of <see cref="ModelGenerator"/> —
/// the layer that owns the dialect flag and decides what each renderer is handed — because that is
/// the only place a dialect branch could be introduced. Asking it of
/// <see cref="EntityPairRenderer"/> would prove nothing: that method takes no dialect, so rendering
/// twice would compare a function to itself.
/// </para>
/// <para>
/// What is deliberately <em>not</em> asserted here: that a navigation never becomes a column
/// mapping. It cannot. The dialect-aware renderer walks <c>model.Fields</c>, and a navigation is
/// never one — it arrives on a separate argument derived from the whole model set. An assertion
/// that a navigation is absent from the column mappings would hold no matter what anyone changed
/// in that walk, which makes it a test that cannot fail rather than a guarantee.
/// </para>
/// </remarks>
public class OneToOneDialectSymmetryTests
{
    private const string Fixture = "one-to-one-unique.m3l.md";

    private static IReadOnlyList<ResolvedModel> Load() =>
        new InterfaceResolver(new M3lLoader().LoadFile(
            Path.Combine(AppContext.BaseDirectory, "fixtures", Fixture))).ResolveAll().ToList();

    /// <summary>Runs the generator into a throwaway directory and returns what it wrote.</summary>
    private static Dictionary<string, string> GenerateInto(bool postgresNaming)
    {
        var root = Path.Combine(Path.GetTempPath(), "mdd-dialect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            new ModelGenerator(new ModelGeneratorOptions
            {
                ProjectPath = root,
                Namespace = "Probe.Entities",
                DbContextName = "ProbeDbContext",
                PostgresNaming = postgresNaming,
            }).Generate(new GeneratorContext { Models = Load(), WorkingDirectory = root });

            return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .ToDictionary(
                    f => Path.GetRelativePath(root, f).Replace('\\', '/'),
                    File.ReadAllText,
                    StringComparer.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Every generated entity file — navigation included — is byte-identical across the dialects.
    /// </summary>
    [Fact]
    public void The_generated_entities_are_identical_in_both_dialects()
    {
        var tsql = GenerateInto(postgresNaming: false);
        var postgres = GenerateInto(postgresNaming: true);

        var entityFiles = tsql.Keys.Where(k => k.StartsWith("Entity_gen/", StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();

        // The navigation this phase added has to be among what is being compared, or the comparison
        // is over files that were never at issue.
        Assert.Contains("Entity_gen/AssetMaintenanceProfileExt.cs", entityFiles);
        Assert.Contains("public AssetExt Asset { get; set; } = null!;",
            tsql["Entity_gen/AssetMaintenanceProfileExt.cs"], StringComparison.Ordinal);

        foreach (var file in entityFiles)
        {
            Assert.True(postgres.ContainsKey(file), $"'{file}' was generated for one dialect only.");
            Assert.Equal(tsql[file], postgres[file]);
        }
    }

    /// <summary>
    /// The negative control: the dialect flag did reach the generator, and changed the DbContext.
    /// </summary>
    /// <remarks>
    /// Without this, the assertion above would pass just as happily if <c>PostgresNaming</c> were
    /// ignored outright — "identical because nothing branches on the dialect" and "identical
    /// because the dialect was never applied" look the same from the entity files alone.
    /// </remarks>
    [Fact]
    public void The_dialect_flag_reached_the_generator_and_changed_the_context()
    {
        var tsql = GenerateInto(postgresNaming: false);
        var postgres = GenerateInto(postgresNaming: true);

        var contextFile = tsql.Keys.Single(k => k.StartsWith("DbContext_gen/", StringComparison.Ordinal));

        Assert.NotEqual(tsql[contextFile], postgres[contextFile]);
        Assert.Contains("e.ToView(\"asset_maintenance_profile\");", postgres[contextFile], StringComparison.Ordinal);
        Assert.DoesNotContain("asset_maintenance_profile", tsql[contextFile], StringComparison.Ordinal);
    }

    /// <summary>
    /// The unique key that makes the relationship one-to-one survives the Postgres dialect, under
    /// the snake_case name.
    /// </summary>
    /// <remarks>
    /// Nothing is emitted to configure the relationship — EF infers it, and it infers
    /// <em>one-to-one</em> only because the foreign key is unique. That index is emitted by the
    /// dialect-aware renderer, so unlike the navigation itself it genuinely can differ per dialect:
    /// were it dropped in one of them, that dialect's model would quietly become one-to-many and
    /// the generated navigation would stop meaning what it says.
    /// </remarks>
    [Fact]
    public void The_unique_key_behind_the_relationship_survives_the_postgres_dialect()
    {
        var output = DbContextRenderer.Render(Load(), "ProbeDbContext", "Probe.Entities", postgresNaming: true);

        Assert.Contains(
            "e.HasIndex(x => x.AssetId).IsUnique().HasDatabaseName(\"uq_asset_maintenance_profile_asset_id\");",
            output, StringComparison.Ordinal);
        Assert.Contains(
            "e.HasIndex(x => x.OrganizationId).IsUnique().HasDatabaseName(\"uq_contractor_organization_id\");",
            output, StringComparison.Ordinal);
    }
}
