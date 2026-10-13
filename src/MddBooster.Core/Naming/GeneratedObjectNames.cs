namespace MddBooster.Core.Naming;

/// <summary>
/// The name the generators give an index or a constraint the model does not name:
/// <c>{prefix}_{owner}_{columns}</c>.
/// </summary>
/// <remarks>
/// Four places need it: both SQL dialects write it into the schema, the Model target repeats it in
/// <c>HasDatabaseName</c> — which is only right while it matches what the schema holds — and the semantic analyzer
/// needs it to tell a name the model declares from one the generators would give another index. Written out in each,
/// the four could drift apart with nothing to report it; here they cannot.
/// <para>
/// The owner and column tokens are the dialect's own, and the caller supplies them: T-SQL writes the model name and
/// PascalCase columns (<c>IX_Order_CustomerId</c>), PostgreSQL the table name and the field names as written
/// (<c>ix_order_customer_id</c>).
/// </para>
/// </remarks>
public static class GeneratedObjectNames
{
    /// <summary>What the name is for. T-SQL names only <see cref="Unique"/>, <see cref="Index"/> and
    /// <see cref="Check"/>; PostgreSQL names every kind.</summary>
    public enum Kind
    {
        PrimaryKey,
        ForeignKey,
        Unique,
        Index,
        Check,
    }

    public static string For(bool postgres, Kind kind, string owner, IEnumerable<string> columns)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        ArgumentNullException.ThrowIfNull(columns);

        var prefix = kind switch
        {
            Kind.PrimaryKey => postgres ? "pk" : "PK",
            Kind.ForeignKey => postgres ? "fk" : "FK",
            Kind.Unique => postgres ? "uq" : "UK",
            Kind.Index => postgres ? "ix" : "IX",
            Kind.Check => postgres ? "ck" : "CK",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var joined = string.Join("_", columns);
        return joined.Length == 0 ? $"{prefix}_{owner}" : $"{prefix}_{owner}_{joined}";
    }

    /// <inheritdoc cref="For(bool, Kind, string, IEnumerable{string})"/>
    public static string For(bool postgres, Kind kind, string owner, params string[] columns) =>
        For(postgres, kind, owner, (IEnumerable<string>)columns);
}
