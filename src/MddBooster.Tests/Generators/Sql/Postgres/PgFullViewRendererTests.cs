using M3L.Native;
using MddBooster.Generators.Sql.Postgres;

namespace MddBooster.Tests.Generators.Sql.Postgres;

public class PgFullViewRendererTests
{
    // `RollupDef` is constructed directly, same seam as `FullViewRendererTests` uses for
    // the T-SQL renderer — the point here is the generator's own handling of an
    // already-parsed `Where` value.

    [Fact]
    public void Rollup_where_clause_dollar_parent_token_resolves_to_the_base_alias()
    {
        var def = new RollupDef
        {
            Target = "Order",
            Fk = "customer_id",
            Aggregate = "count",
            Where = "$parent.production_state = 'active'",
        };

        var sql = PgFullViewRenderer.RenderRollupSubquery(
            def, "public", "b", "id",
            tableNameMap: new Dictionary<string, string> { ["Order"] = "order" },
            viewNameMap: new Dictionary<string, string>(),
            derivedFieldsByModel: new Dictionary<string, IReadOnlySet<string>>());

        Assert.Equal(
            "(SELECT COUNT(*) FROM public.order WHERE customer_id = b.id AND (b.production_state = 'active'))",
            sql);
    }

    [Fact]
    public void Rollup_where_clause_referencing_the_base_alias_directly_is_unaffected()
    {
        // PG's `where:` was already a raw pass-through with no identifier rewriting, so a
        // hand-written `b.<field>` reference already worked — this pins that it still does
        // now that `$parent.` substitution runs first.
        var def = new RollupDef
        {
            Target = "Order",
            Fk = "customer_id",
            Aggregate = "count",
            Where = "b.production_state = 'active'",
        };

        var sql = PgFullViewRenderer.RenderRollupSubquery(
            def, "public", "b", "id",
            tableNameMap: new Dictionary<string, string> { ["Order"] = "order" },
            viewNameMap: new Dictionary<string, string>(),
            derivedFieldsByModel: new Dictionary<string, IReadOnlySet<string>>());

        Assert.Equal(
            "(SELECT COUNT(*) FROM public.order WHERE customer_id = b.id AND (b.production_state = 'active'))",
            sql);
    }
}
