using M3L.Native;

namespace MddBooster.Generators.Sql;

/// <summary>
/// A computed column has the type its field declares, on both platforms. One portable expression yields different types
/// per dialect — <c>floor</c> of a decimal is a decimal on SQL Server and a numeric on PostgreSQL, <c>date_add</c> of a date
/// a date on one and a timestamp on the other — while the read type maps the declared one, so a value of these types is
/// cast to it.
/// </summary>
/// <remarks>
/// Text is not cast: a cast to a declared length would cut a longer value instead of refusing it. A boolean is not cast
/// here: SQL Server's bit is the translator's (it also turns a predicate into a value), and PostgreSQL's predicates are
/// already boolean.
/// </remarks>
internal static class ComputedValues
{
    private static readonly HashSet<string> CastTypes = new(StringComparer.Ordinal)
    {
        "identifier", "integer", "long", "short", "byte", "float", "double", "decimal", "date", "time", "timestamp", "datetime",
    };

    internal static bool IsCast(FieldNode field) => field.Type is { } type && CastTypes.Contains(type);
}
