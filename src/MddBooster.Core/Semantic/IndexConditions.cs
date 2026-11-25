using System.Text;
using MddBooster.Core.Naming;

namespace MddBooster.Core.Semantic;

/// <summary>
/// The condition of a conditional index — <c>@index(…, where: "…")</c> / <c>@unique(…, where: "…")</c> (M3L §3.3.5) — as
/// each SQL dialect and the Model (EF) target write it. One place, so the index the SQL targets create and the filter the
/// EF model declares are the same text.
/// </summary>
/// <remarks>
/// <para>
/// The language admits any portable expression; what a database can put on an index is narrower, and narrowest on SQL
/// Server, whose filtered index takes only <c>AND</c> of comparisons between a column and a constant, <c>IN</c> lists of
/// constants and <c>IS [NOT] NULL</c> (no <c>OR</c>, no <c>NOT</c>, no function, no column against column). PostgreSQL
/// takes more but no function that is not immutable (<c>now()</c>, <c>today()</c>). This generator emits the same
/// condition to both dialects, so it holds every condition to SQL Server's form: a model that builds is one both
/// dialects index the same way. <see cref="Unindexable"/> says what falls outside it.
/// </para>
/// </remarks>
public static class IndexConditions
{
    private static readonly HashSet<string> Comparisons = new(StringComparer.Ordinal) { "=", "!=", "<>", "<", "<=", ">", ">=" };

    /// <summary>
    /// Why <paramref name="expression"/> cannot be an index condition, or <see langword="null"/> when it can: a conjunction
    /// (<c>AND</c>) of <c>field op constant</c>, <c>field IN (constant, …)</c>, <c>field IS [NOT] NULL</c> and a boolean field
    /// standing alone. Field names are not resolved here.
    /// </summary>
    public static string? Unindexable(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var tokens = Tokenize(expression);
        if (tokens is null) return "the expression could not be read";
        if (tokens.Count == 0) return "the condition is empty";

        var pos = 0;
        while (true)
        {
            if (Conjunct(tokens, ref pos) is { } why) return why;
            if (pos == tokens.Count) return null;
            if (!IsWord(tokens[pos], "and"))
                return IsWord(tokens[pos], "or")
                    ? "OR (an index condition is a conjunction — AND only)"
                    : $"'{tokens[pos]}' where AND or the end was expected";
            pos++;
        }
    }

    /// <summary>
    /// The SQL Server text of an entry's condition: the declared condition, and before it the <c>IS NOT NULL</c> filter SQL
    /// Server needs to leave <c>NULL</c> rows out of a unique index over nullable columns (<paramref name="notNullColumns"/>,
    /// empty when none is needed), joined with <c>AND</c>. <see langword="null"/> when there is neither.
    /// </summary>
    /// <param name="column">Spells a field name as the bracketed column.</param>
    public static string? SqlServer(string? where, IReadOnlyList<string> notNullColumns, Func<string, string> column)
    {
        ArgumentNullException.ThrowIfNull(notNullColumns);
        ArgumentNullException.ThrowIfNull(column);
        var notNull = notNullColumns.Count == 0 ? null : string.Join(" AND ", notNullColumns.Select(c => $"{column(c)} IS NOT NULL"));
        if (where is null) return notNull;
        var condition = Translate(where, SqlDialect.SqlServer, column);
        // Both are flat conjunctions (Unindexable holds the declared one to that), so they join without parentheses —
        // SQL Server's filtered index grammar names none.
        return notNull is null ? condition : $"{notNull} AND {condition}";
    }

    /// <summary>The PostgreSQL text of a declared condition.</summary>
    /// <param name="column">Spells a field name as the column.</param>
    public static string PostgreSql(string where, Func<string, string> column) => Translate(where, SqlDialect.PostgreSql, column);

    private static string Translate(string where, SqlDialect dialect, Func<string, string> column)
    {
        var result = PortableExpression.Translate(where, dialect, column, condition: true);
        return result.Sql ?? throw new InvalidOperationException(
            $"An index condition outside the portable subset reached the renderer: {where}");
    }

    /// <summary>The bracketed SQL Server column of a field name.</summary>
    public static string SqlServerColumn(string field) => $"[{NameCasing.ToPascalCase(field)}]";

    private static string? Conjunct(List<string> t, ref int pos)
    {
        if (pos == t.Count) return "a condition was expected after AND";
        if (IsWord(t[pos], "not")) return "NOT (write the comparison it negates, e.g. != or IS NOT NULL)";
        if (t[pos] == "(") return "a parenthesised condition (an index condition is a flat conjunction)";
        if (!IsName(t[pos])) return $"'{t[pos]}' where a field name was expected (an index condition compares a field with a constant)";
        pos++;

        // A boolean field standing alone.
        if (pos == t.Count || IsWord(t[pos], "and") || IsWord(t[pos], "or")) return null;

        if (t[pos] == "(") return $"the function '{t[pos - 1]}' (an index condition compares a field with a constant)";

        if (IsWord(t[pos], "is"))
        {
            pos++;
            if (pos < t.Count && IsWord(t[pos], "not")) pos++;
            if (pos < t.Count && IsWord(t[pos], "null")) { pos++; return null; }
            return "IS must be followed by NULL or NOT NULL";
        }

        if (IsWord(t[pos], "in"))
        {
            pos++;
            if (pos == t.Count || t[pos] != "(") return "IN must be followed by a list of constants";
            pos++;
            while (true)
            {
                if (pos == t.Count || !IsConstant(t[pos])) return "an IN list holds constants only";
                pos++;
                if (pos < t.Count && t[pos] == ",") { pos++; continue; }
                if (pos < t.Count && t[pos] == ")") { pos++; return null; }
                return "an IN list must close with )";
            }
        }

        if (IsWord(t[pos], "not")) return "NOT IN / NOT LIKE (SQL Server's filtered index takes neither)";
        if (IsWord(t[pos], "like")) return "LIKE (an index condition compares a field with a constant)";
        if (IsWord(t[pos], "between")) return "BETWEEN (write it as two comparisons joined by AND)";
        if (!Comparisons.Contains(t[pos])) return $"'{t[pos]}' where a comparison was expected";
        pos++;
        if (pos == t.Count) return "a constant was expected after the comparison";
        if (IsWord(t[pos], "null")) return "a comparison with NULL (write IS NULL or IS NOT NULL)";
        if (!IsConstant(t[pos]))
            return IsName(t[pos])
                ? $"'{t[pos]}' (an index condition compares a field with a constant, not with another field or a function)"
                : $"'{t[pos]}' where a constant was expected";
        pos++;
        if (pos < t.Count && t[pos] == "(") return $"the function '{t[pos - 1]}' (an index condition compares a field with a constant)";
        return pos < t.Count && !IsWord(t[pos], "and") && !IsWord(t[pos], "or")
            ? $"'{t[pos]}' after the constant (arithmetic is not part of an index condition)"
            : null;
    }

    private static bool IsWord(string token, string word) => string.Equals(token, word, StringComparison.OrdinalIgnoreCase);

    private static bool IsConstant(string token) =>
        token.StartsWith('\'') || char.IsDigit(token[0]) || (token[0] == '-' && token.Length > 1 && char.IsDigit(token[1]))
        || IsWord(token, "true") || IsWord(token, "false");

    private static bool IsName(string token) =>
        (char.IsLetter(token[0]) || token[0] == '_' || token[0] == '$')
        && !IsWord(token, "true") && !IsWord(token, "false") && !IsWord(token, "null")
        && !IsWord(token, "and") && !IsWord(token, "or") && !IsWord(token, "not") && !IsWord(token, "is")
        && !IsWord(token, "in") && !IsWord(token, "like") && !IsWord(token, "between");

    // Names (with dots and a leading $), numbers (a leading minus folded in where no value precedes it), single-quoted
    // strings ('' inside), comparison operators, parentheses, commas. Anything else is kept as one-character tokens, which
    // the grammar then names.
    private static List<string>? Tokenize(string s)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '\'')
            {
                var sb = new StringBuilder("'");
                i++;
                while (true)
                {
                    if (i >= s.Length) return null;
                    if (s[i] == '\'')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '\'') { sb.Append("''"); i += 2; continue; }
                        sb.Append('\''); i++; break;
                    }
                    sb.Append(s[i++]);
                }
                tokens.Add(sb.ToString());
                continue;
            }
            var negative = c == '-' && i + 1 < s.Length && char.IsDigit(s[i + 1])
                           && (tokens.Count == 0 || Comparisons.Contains(tokens[^1]) || tokens[^1] is "(" or ",");
            if (char.IsDigit(c) || negative)
            {
                var start = i++;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                tokens.Add(s[start..i]);
                continue;
            }
            if (char.IsLetter(c) || c == '_' || c == '$')
            {
                var start = i++;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '.')) i++;
                tokens.Add(s[start..i]);
                continue;
            }
            if (i + 1 < s.Length && Comparisons.Contains(s.Substring(i, 2))) { tokens.Add(s.Substring(i, 2)); i += 2; continue; }
            tokens.Add(c.ToString());
            i++;
        }
        return tokens;
    }
}
