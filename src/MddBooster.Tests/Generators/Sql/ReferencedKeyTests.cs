using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Sql;

namespace MddBooster.Tests.Generators.Sql;

/// <summary>
/// Every T-SQL statement that points at another model's key — the FK clause, a lookup JOIN, a
/// rollup's correlation — names that model's actual <c>@pk</c> column in the configured schema.
/// The Postgres renderer already did; T-SQL assumed <c>[dbo]</c> and <c>[Id]</c>, which is a
/// column a <c>code @pk</c> table does not have.
/// </summary>
public class ReferencedKeyTests
{
    private const string Schema = "app";

    private const string Source = """
        # Namespace: test.refkey

        ## Country

        - code: string(2) @pk
        - name: string(50)
        - city_count: integer @rollup(City.country_code, count)

        ---

        ## City

        - id: identifier @pk @generated
        - country_code: string(2) @reference(Country)
        - country_name: string @lookup(country_code.name)
        """;

    private static IReadOnlyList<ResolvedModel> Load()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-refkey-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, Source);
        try
        {
            return new InterfaceResolver(new M3lLoader().LoadFile(tmp)).ResolveAll();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Foreign_key_names_the_targets_own_key_in_the_configured_schema()
    {
        var models = Load();

        var sql = TableRenderer.Render(models.Single(m => m.Name == "City"), Schema, allModels: models);

        Assert.Contains("[CountryCode] NVARCHAR(2) NOT NULL REFERENCES [app].[Country]([Code])", sql);
        Assert.DoesNotContain("[dbo]", sql);
        Assert.DoesNotContain("([Id])", sql);
    }

    [Fact]
    public void Lookup_join_matches_on_the_targets_own_key()
    {
        var models = Load();
        var plan = new ViewPlanner().Plan(models.Single(m => m.Name == "City"));

        var sql = FullViewRenderer.Render(plan, Schema, allModels: models);

        Assert.Matches(@"LEFT JOIN \[app\]\.\[Country\] AS (\w+) ON b\.\[CountryCode\] = \1\.\[Code\]", sql);
        Assert.DoesNotMatch(@"= \w+\.\[Id\]", sql); // City's own [Id] is projected, never joined on
    }

    [Fact]
    public void Rollup_correlates_on_the_owning_models_own_key()
    {
        var models = Load();
        var plan = new ViewPlanner().Plan(models.Single(m => m.Name == "Country"));

        var sql = FullViewRenderer.Render(plan, Schema, allModels: models);

        Assert.Contains("FROM [app].[City] WHERE [CountryCode] = b.[Code]", sql);
        Assert.DoesNotContain(".[Id]", sql);
    }

    [Fact]
    public void A_reference_whose_target_is_not_supplied_fails_instead_of_guessing_a_key()
    {
        var city = Load().Single(m => m.Name == "City");

        var ex = Assert.Throws<InvalidOperationException>(() => TableRenderer.Render(city, Schema));
        Assert.Contains("Country", ex.Message);
    }

    [Fact]
    public void Post_deployment_refresh_orders_and_names_views_in_any_schema()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mdd-postdeploy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "A.sql"), "CREATE VIEW [app].[A] AS SELECT * FROM [app].[B]\nGO\n");
            File.WriteAllText(Path.Combine(dir, "B.sql"), "CREATE VIEW [app].[B] AS SELECT 1 AS [X]\nGO\n");

            var script = PostDeploymentScriptRenderer.Render(dir);

            var b = script.IndexOf("EXEC sp_refreshview N'[app].[B]';", StringComparison.Ordinal);
            var a = script.IndexOf("EXEC sp_refreshview N'[app].[A]';", StringComparison.Ordinal);
            Assert.True(b >= 0 && a > b, script);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
