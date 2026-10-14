using System.Text;

namespace MddBooster.Core.Semantic;

/// <summary>
/// Translates an expression written in M3L's portable subset (§10.7) into one SQL dialect: literals, field references,
/// operators, <c>CASE</c> and the closed list of functions, each with the meaning the language gives it.
/// </summary>
/// <remarks>
/// <para>
/// The language defines what a portable expression means; how a platform spells it is this generator's mapping. An
/// expression that uses anything outside the subset is not guessed at: it comes back as <see cref="Result.Unsupported"/>
/// naming what could not be translated, and the caller reports it (a model reaches a platform feature by declaring the
/// expression for that platform instead).
/// </para>
/// <para>
/// Field references go through a callback — how a column is spelled (quoted, cased, qualified by an alias) belongs to the
/// renderer that knows the query it sits in.
/// </para>
/// </remarks>
public static class PortableExpression
{
    /// <param name="Sql">The translated expression, or <c>null</c> when something could not be translated.</param>
    /// <param name="Unsupported">What could not be translated, each named once.</param>
    public sealed record Result(string? Sql, IReadOnlyList<string> Unsupported);

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "not", "is", "null", "in", "between", "like", "case", "when", "then", "else", "end", "true", "false",
    };

    private static readonly HashSet<string> PredicateKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "not", "is", "in", "between", "like",
    };

    private static readonly HashSet<string> Units = new(StringComparer.Ordinal)
    {
        "year", "month", "day", "hour", "minute", "second",
    };

    private enum Kind { Text, Number, Name, Operator, Open, Close, Comma }

    private sealed record Token(Kind Kind, string Value);

    private abstract record Node;
    private sealed record Leaf(Token Token) : Node;
    private sealed record Call(string Name, List<List<Node>> Args) : Node;
    private sealed record Group(List<List<Node>> Items) : Node;

    /// <summary>Translates <paramref name="expression"/> into <paramref name="dialect"/>.</summary>
    /// <param name="column">Spells a field reference (<c>name</c>, <c>Relation.field</c>, <c>$parent.field</c>) as a column.</param>
    /// <param name="booleanResult">
    /// The expression is a boolean field's value. SQL Server's select list takes no predicate, so a predicate there becomes
    /// <c>CASE WHEN … THEN 1 ELSE 0 END</c> (as <c>bit</c>).
    /// </param>
    /// <param name="fieldType">
    /// The M3L type of the field a reference names, or <c>null</c> when it is not known. Without it a field's type is not
    /// known, and only what literals and functions produce is: whether <c>+</c> stands between text, whether
    /// <c>date_add</c> moves a date.
    /// </param>
    /// <param name="outOfScope">
    /// Why a field reference names nothing the expression's context can read, or <c>null</c> when it does. Without it
    /// every name is taken as a field.
    /// </param>
    /// <param name="condition">
    /// The expression is a condition (a filter), not a value. SQL Server takes no bit there, so a boolean field standing
    /// alone as a condition is compared with 1.
    /// </param>
    public static Result Translate(string expression, SqlDialect dialect, Func<string, string> column, bool booleanResult = false,
        Func<string, string?>? fieldType = null, bool condition = false, Func<string, string?>? outOfScope = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(column);

        var unsupported = new List<string>();
        void Note(string what)
        {
            if (!unsupported.Contains(what)) unsupported.Add(what);
        }

        var tokens = Tokenize(expression, Note);
        var position = 0;
        var nodes = ParseSequence(tokens, ref position, Note);
        if (position < tokens.Count) Note($"unbalanced '{tokens[position].Value}'");

        var context = new RenderContext(dialect, column, Note, fieldType ?? (_ => null), outOfScope ?? (_ => null));
        // A boolean field's predicate is selected through a CASE, whose WHEN is a condition.
        var sql = Render(nodes, context, condition || (booleanResult && IsPredicate(nodes)));
        if (unsupported.Count > 0) return new Result(null, unsupported);

        if (dialect == SqlDialect.SqlServer && booleanResult)
        {
            // SQL Server selects no predicate, and 1 and 0 alone are int — a boolean property is read from a bit.
            sql = IsPredicate(nodes)
                ? $"CASE WHEN {sql} THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END"
                : $"CAST({sql} AS bit)";
        }
        return new Result(sql, unsupported);
    }

    /// <summary>
    /// The field references an expression names — every name that is not a keyword or a function — in the order they
    /// appear. Read the way <see cref="Translate"/> reads them, so it answers for platform text too, as far as that text
    /// is made of the same words.
    /// </summary>
    public static IReadOnlyList<string> FieldNames(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var tokens = Tokenize(expression, _ => { });
        var names = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind != Kind.Name || Keywords.Contains(t.Value)) continue;
            if (i + 1 < tokens.Count && tokens[i + 1].Kind == Kind.Open) continue;
            if (!names.Contains(t.Value)) names.Add(t.Value);
        }
        return names;
    }

    private sealed record RenderContext(SqlDialect Dialect, Func<string, string> Column, Action<string> Note, Func<string, string?> FieldType,
        Func<string, string?> OutOfScope)
    {
        public bool SqlServer => Dialect == SqlDialect.SqlServer;
    }

    /// <summary>The M3L types that hold text — what <c>+</c> must not stand between (§10.7.2).</summary>
    private static readonly HashSet<string> TextTypes = new(StringComparer.Ordinal)
    {
        "string", "text", "email", "phone", "url",
    };

    // ---- tokens ----

    private static List<Token> Tokenize(string expression, Action<string> note)
    {
        var tokens = new List<Token>();
        var s = expression;
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            var start = i;
            if (c == '\'')
            {
                i++;
                var closed = false;
                while (i < s.Length)
                {
                    if (s[i] == '\'')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '\'') { i += 2; continue; }
                        closed = true;
                        break;
                    }
                    i++;
                }
                if (!closed)
                {
                    // Emitted, an open literal would run on into whatever the generator writes after the expression.
                    note($"unterminated string literal {s[start..]}");
                    break;
                }
                i++;
                tokens.Add(new Token(Kind.Text, s[start..i]));
            }
            else if (char.IsAsciiDigit(c))
            {
                // §10.7.1: digits, with at most one decimal point that digits follow.
                while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                if (i + 1 < s.Length && s[i] == '.' && char.IsAsciiDigit(s[i + 1]))
                {
                    i++;
                    while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                }
                if (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '.' or '_' or '$'))
                {
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '.' or '_' or '$')) i++;
                    note($"malformed number {s[start..i]}");
                }
                tokens.Add(new Token(Kind.Number, s[start..i]));
            }
            else if (char.IsLetter(c) || c == '_' || c == '$')
            {
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '.' or '$')) i++;
                tokens.Add(new Token(Kind.Name, s[start..i]));
            }
            else if (c is '[' or '"' or '`')
            {
                var close = c == '[' ? ']' : c;
                i++;
                while (i < s.Length && s[i] != close) i++;
                i = Math.Min(i + 1, s.Length);
                note($"{(c == '[' ? "bracketed" : "quoted")} identifier {s[start..i]}");
                // Kept as what it stands for, so the rest is read in the shape the author wrote it.
                tokens.Add(new Token(Kind.Name, s[start..i]));
            }
            else
            {
                var two = i + 1 < s.Length ? s.Substring(i, 2) : c.ToString();
                if (two is "->" or "||" or "::")
                {
                    note(two == "->" ? "JSON operator ->" : two == "||" ? "|| (write concat(…))" : ":: cast");
                    i += 2;
                    if (two == "->" && i < s.Length && s[i] == '>') i++;
                    tokens.Add(new Token(Kind.Operator, s[start..i]));
                    continue;
                }
                if (two is "<>" or "!=" or "<=" or ">=")
                {
                    tokens.Add(new Token(Kind.Operator, two == "!=" ? "<>" : two));
                    i += 2;
                    continue;
                }
                i++;
                switch (c)
                {
                    case '(': tokens.Add(new Token(Kind.Open, "(")); break;
                    case ')': tokens.Add(new Token(Kind.Close, ")")); break;
                    case ',': tokens.Add(new Token(Kind.Comma, ",")); break;
                    case '+' or '-' or '*' or '/' or '%' or '=' or '<' or '>':
                        tokens.Add(new Token(Kind.Operator, c.ToString()));
                        break;
                    default:
                        note($"'{c}'");
                        tokens.Add(new Token(Kind.Operator, c.ToString()));
                        break;
                }
            }
        }
        return tokens;
    }

    // ---- structure: calls and parenthesised lists ----

    /// <summary>Nodes up to a closing parenthesis or comma at this level (not consumed).</summary>
    private static List<Node> ParseSequence(List<Token> tokens, ref int position, Action<string> note)
    {
        var nodes = new List<Node>();
        while (position < tokens.Count && tokens[position].Kind is not (Kind.Close or Kind.Comma))
        {
            var token = tokens[position];
            if (token.Kind == Kind.Name && !Keywords.Contains(token.Value)
                && position + 1 < tokens.Count && tokens[position + 1].Kind == Kind.Open)
            {
                position += 2;
                nodes.Add(new Call(token.Value, ParseItems(tokens, ref position, note)));
            }
            else if (token.Kind == Kind.Open)
            {
                position++;
                nodes.Add(new Group(ParseItems(tokens, ref position, note)));
            }
            else
            {
                nodes.Add(new Leaf(token));
                position++;
            }
        }
        return nodes;
    }

    /// <summary>Comma-separated items after an opening parenthesis, through its close.</summary>
    private static List<List<Node>> ParseItems(List<Token> tokens, ref int position, Action<string> note)
    {
        var items = new List<List<Node>>();
        if (position < tokens.Count && tokens[position].Kind == Kind.Close)
        {
            position++;
            return items;
        }
        while (true)
        {
            items.Add(ParseSequence(tokens, ref position, note));
            if (position >= tokens.Count)
            {
                // Closing it for the author would be guessing where the parenthesis was meant to end.
                note("missing ')'");
                return items;
            }
            var t = tokens[position++];
            if (t.Kind == Kind.Close) return items;
        }
    }

    // ---- rendering ----

    private static string Render(List<Node> nodes, RenderContext cx, bool condition = false)
    {
        var sb = new StringBuilder();
        var unary = false;
        var notJoining = RangeAnds(nodes);
        notJoining.UnionWith(ValueWhens(nodes));
        for (var k = 0; k < nodes.Count; k++)
        {
            var node = nodes[k];
            // A CASE standing as a condition yields a value — on SQL Server, a bit to compare with 1.
            if (cx.SqlServer && node is Leaf { Token: { Kind: Kind.Name } c } && c.Value.Equals("case", StringComparison.OrdinalIgnoreCase)
                && EndOf(nodes, k) is { } end && IsConditionSpan(nodes, k, end, condition, notJoining))
            {
                if (sb.Length > 0 && !unary) sb.Append(' ');
                sb.Append('(').Append(Render(nodes.GetRange(k, end - k + 1), cx)).Append(" = 1)");
                unary = false;
                k = end;
                continue;
            }
            if (node is Leaf { Token: { Kind: Kind.Operator, Value: "+" } }
                && (IsText(nodes, k - 1, cx) || IsText(nodes, k + 1, cx)))
                cx.Note("+ joining text (write concat(…))");
            // The subset joins operands with an operator or a keyword. Two side by side is a word it does not have,
            // written where an operator belongs.
            if (k > 0 && IsOperand(nodes[k - 1]) && IsOperand(node))
                cx.Note($"{Describe(nodes[k - 1])} {Describe(node)} (no operator between them)");
            var piece = cx.SqlServer && IsConditionAt(nodes, k, condition, notJoining)
                ? RenderCondition(node, cx)
                : RenderNode(node, cx);
            // Glued, two minus signs open a line comment that ends the expression early.
            if (sb.Length > 0 && (!unary || (sb[^1] == '-' && piece.StartsWith('-')))) sb.Append(' ');
            sb.Append(piece);
            // A sign with nothing to subtract from — first, or after an operator, keyword or comma — binds to what follows.
            unary = node is Leaf { Token: { Kind: Kind.Operator, Value: "-" or "+" } }
                && (k == 0 || nodes[k - 1] is Leaf { Token.Kind: Kind.Operator } || nodes[k - 1] is Leaf { Token: { Kind: Kind.Name } p } && Keywords.Contains(p.Value));
        }
        return sb.ToString();
    }

    private static readonly HashSet<string> ConditionJoinsBefore = new(StringComparer.OrdinalIgnoreCase) { "and", "or", "not", "when" };
    private static readonly HashSet<string> ConditionJoinsAfter = new(StringComparer.OrdinalIgnoreCase) { "and", "or", "then" };

    /// <summary>
    /// Whether the node at <paramref name="k"/> stands as a condition: between the sequence's edges (when the sequence is
    /// one) and the words that join conditions — AND, OR, NOT, WHEN before it; AND, OR, THEN after. A BETWEEN's AND joins
    /// the two ends of a range, not conditions.
    /// </summary>
    private static bool IsConditionAt(List<Node> nodes, int k, bool condition, HashSet<int> notJoining)
        => IsConditionSpan(nodes, k, k, condition, notJoining);

    /// <summary>Whether the nodes from <paramref name="first"/> to <paramref name="last"/> stand together as a condition.</summary>
    private static bool IsConditionSpan(List<Node> nodes, int first, int last, bool condition, HashSet<int> notJoining)
    {
        bool Joins(int i, HashSet<string> words)
            => !notJoining.Contains(i) && nodes[i] is Leaf { Token: { Kind: Kind.Name } t } && words.Contains(t.Value);
        var before = first == 0 ? condition : Joins(first - 1, ConditionJoinsBefore);
        var after = last == nodes.Count - 1 ? condition : Joins(last + 1, ConditionJoinsAfter);
        return before && after;
    }

    /// <summary>The END that closes the CASE at <paramref name="k"/>, or <c>null</c> when it is not closed here.</summary>
    private static int? EndOf(List<Node> nodes, int k)
    {
        var depth = 0;
        for (var i = k; i < nodes.Count; i++)
        {
            if (nodes[i] is not Leaf { Token: { Kind: Kind.Name } t }) continue;
            if (t.Value.Equals("case", StringComparison.OrdinalIgnoreCase)) depth++;
            else if (t.Value.Equals("end", StringComparison.OrdinalIgnoreCase) && --depth == 0) return i;
        }
        return null;
    }

    /// <summary>
    /// The positions of the WHENs of a simple CASE (<c>CASE x WHEN value THEN …</c>) — those take a value to compare
    /// with, not a condition.
    /// </summary>
    private static HashSet<int> ValueWhens(List<Node> nodes)
    {
        var whens = new HashSet<int>();
        var simple = new Stack<bool>();
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is not Leaf { Token: { Kind: Kind.Name } t }) continue;
            if (t.Value.Equals("case", StringComparison.OrdinalIgnoreCase))
                simple.Push(!(i + 1 < nodes.Count && nodes[i + 1] is Leaf { Token: { Kind: Kind.Name } w }
                              && w.Value.Equals("when", StringComparison.OrdinalIgnoreCase)));
            else if (t.Value.Equals("end", StringComparison.OrdinalIgnoreCase) && simple.Count > 0) simple.Pop();
            else if (t.Value.Equals("when", StringComparison.OrdinalIgnoreCase) && simple.Count > 0 && simple.Peek()) whens.Add(i);
        }
        return whens;
    }

    /// <summary>The positions of the ANDs that close a BETWEEN's range.</summary>
    private static HashSet<int> RangeAnds(List<Node> nodes)
    {
        var ands = new HashSet<int>();
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is not Leaf { Token: { Kind: Kind.Name } t } || !t.Value.Equals("between", StringComparison.OrdinalIgnoreCase)) continue;
            for (var j = i + 1; j < nodes.Count; j++)
            {
                if (nodes[j] is not Leaf { Token: { Kind: Kind.Name } a } || !a.Value.Equals("and", StringComparison.OrdinalIgnoreCase)) continue;
                ands.Add(j);
                break;
            }
        }
        return ands;
    }

    /// <summary>
    /// A node standing as a condition on SQL Server: a bare field — which a condition can only be if it is boolean — is
    /// compared with 1, a boolean literal becomes a comparison that holds or does not, and parentheses carry the condition
    /// inside. Anything else is already a condition or is not one, and renders as it is.
    /// </summary>
    private static string RenderCondition(Node node, RenderContext cx) => node switch
    {
        Leaf { Token: { Kind: Kind.Name } t } when t.Value.Equals("true", StringComparison.OrdinalIgnoreCase) => "1 = 1",
        Leaf { Token: { Kind: Kind.Name } t } when t.Value.Equals("false", StringComparison.OrdinalIgnoreCase) => "1 = 0",
        Leaf { Token: { Kind: Kind.Name } t } when !Keywords.Contains(t.Value) => $"({RenderLeaf(t, cx)} = 1)",
        Group { Items: [var only] } => "(" + Render(only, cx, condition: true) + ")",
        // No function of the subset is a predicate: a call standing as a condition yields a value (coalesce of booleans).
        Call => "(" + RenderNode(node, cx) + " = 1)",
        _ => RenderNode(node, cx),
    };

    private static readonly HashSet<string> TextFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "concat", "lower", "upper", "trim", "replace", "substring",
    };

    /// <summary>Whether the node is known to be text: a literal, a function that returns text, a field the caller says holds text.</summary>
    private static bool IsText(List<Node> nodes, int index, RenderContext cx)
        => index >= 0 && index < nodes.Count && nodes[index] switch
        {
            Leaf { Token.Kind: Kind.Text } => true,
            Leaf { Token: { Kind: Kind.Name } t } => !Keywords.Contains(t.Value) && cx.FieldType(t.Value) is { } type && TextTypes.Contains(type),
            Call call => TextFunctions.Contains(call.Name),
            _ => false,
        };

    /// <summary>A value: a literal, a field, a call, a parenthesised list — or <c>true</c>, <c>false</c>, <c>null</c>.</summary>
    private static bool IsOperand(Node node) => node switch
    {
        Leaf { Token.Kind: Kind.Text or Kind.Number } => true,
        Leaf { Token: { Kind: Kind.Name } t } => !Keywords.Contains(t.Value) || t.Value.ToLowerInvariant() is "true" or "false" or "null",
        Call or Group => true,
        _ => false,
    };

    private static string Describe(Node node) => node switch
    {
        Leaf leaf => leaf.Token.Value,
        Call call => call.Name + "(…)",
        _ => "(…)",
    };

    private static string RenderNode(Node node, RenderContext cx) => node switch
    {
        Leaf leaf => RenderLeaf(leaf.Token, cx),
        Group group => RenderGroup(group, cx),
        Call call => RenderCall(call, cx),
        _ => throw new InvalidOperationException(),
    };

    private static string RenderGroup(Group group, RenderContext cx)
    {
        if (group.Items.Count == 0 || group.Items.Any(i => i.Count == 0)) cx.Note("empty ( )");
        return "(" + string.Join(", ", group.Items.Select(i => Render(i, cx))) + ")";
    }

    private static string RenderLeaf(Token token, RenderContext cx)
    {
        // Without N a T-SQL literal is narrowed to the database's code page — non-Latin text becomes '?'.
        if (token.Kind == Kind.Text) return cx.SqlServer ? "N" + token.Value : token.Value;
        if (token.Kind != Kind.Name) return token.Value;
        var lower = token.Value.ToLowerInvariant();
        if (lower is "true" or "false")
            return cx.SqlServer ? (lower == "true" ? "1" : "0") : lower.ToUpperInvariant();
        if (Keywords.Contains(lower)) return lower.ToUpperInvariant();
        if (cx.OutOfScope(token.Value) is { } why) cx.Note(why);
        return cx.Column(token.Value);
    }

    private static readonly HashSet<string> Functions = new(StringComparer.Ordinal)
    {
        "coalesce", "concat", "nullif", "lower", "upper", "trim", "replace", "abs", "floor", "ceiling", "length",
        "substring", "round", "today", "now", "generate_uuid", "year", "month", "day", "date_add", "date_diff",
    };

    private static string RenderCall(Call call, RenderContext cx)
    {
        var name = call.Name.ToLowerInvariant();
        // Named before its arguments, so what is unsupported reads in the order it is written.
        if (!Functions.Contains(name)) cx.Note($"function {call.Name}()");
        if (call.Args.Any(a => a.Count == 0)) cx.Note($"empty argument in {name}()");
        var args = call.Args.Select(a => Render(a, cx)).ToList();
        string Same(string sqlServer) => $"{(cx.SqlServer ? sqlServer : name)}({string.Join(", ", args)})";
        bool Arity(params int[] counts)
        {
            if (counts.Contains(args.Count)) return true;
            cx.Note($"{name}() takes {string.Join(" or ", counts)} argument(s), not {args.Count}");
            return false;
        }
        bool AtLeast(int count)
        {
            if (args.Count >= count) return true;
            cx.Note($"{name}() takes at least {count} argument(s), not {args.Count}");
            return false;
        }

        switch (name)
        {
            // §10.7.4 writes both as (a, b, …): SQL Server refuses a single argument where PostgreSQL takes it.
            case "coalesce": return AtLeast(2) ? Same("COALESCE") : string.Empty;
            case "concat": return AtLeast(2) ? Same("CONCAT") : string.Empty;
            case "nullif": return Arity(2) ? Same("NULLIF") : string.Empty;
            case "lower": return Arity(1) ? Same("LOWER") : string.Empty;
            case "upper": return Arity(1) ? Same("UPPER") : string.Empty;
            case "trim": return Arity(1) ? Same("TRIM") : string.Empty;
            case "replace": return Arity(3) ? Same("REPLACE") : string.Empty;
            case "abs": return Arity(1) ? Same("ABS") : string.Empty;
            case "floor": return Arity(1) ? Same("FLOOR") : string.Empty;
            case "ceiling": return Arity(1) ? Same("CEILING") : string.Empty;
            case "length":
                if (!Arity(1)) return string.Empty;
                // LEN leaves trailing spaces uncounted; the language means every character. '+' keeps a null null, where
                // CONCAT would read it as empty and give 0.
                return cx.SqlServer ? $"(LEN(CAST({args[0]} AS nvarchar(max)) + N'.') - 1)" : $"length({args[0]})";
            case "substring":
                if (!Arity(3)) return string.Empty;
                return cx.SqlServer
                    ? $"SUBSTRING({args[0]}, {args[1]}, {args[2]})"
                    : $"substring({args[0]} from {args[1]} for {args[2]})";
            case "round":
                if (!Arity(1, 2)) return string.Empty;
                var digits = args.Count == 2 ? args[1] : "0";
                return cx.SqlServer ? $"ROUND({args[0]}, {digits})" : $"round(({args[0]})::numeric, {digits})";
            case "today":
                return Arity(0) ? (cx.SqlServer ? "CAST(SYSDATETIME() AS date)" : "CURRENT_DATE") : string.Empty;
            case "now":
                return Arity(0) ? (cx.SqlServer ? "SYSDATETIMEOFFSET()" : "now()") : string.Empty;
            case "generate_uuid":
                return Arity(0) ? (cx.SqlServer ? "NEWID()" : "gen_random_uuid()") : string.Empty;
            case "year" or "month" or "day":
                if (!Arity(1)) return string.Empty;
                return cx.SqlServer ? $"{name.ToUpperInvariant()}({args[0]})" : $"EXTRACT({name.ToUpperInvariant()} FROM {args[0]})::int";
            case "date_add":
            {
                if (!Arity(3) || Unit(call.Args[2], name, cx) is not { } unit) return string.Empty;
                // A date has no hours: SQL Server refuses to move one by them, PostgreSQL makes it a timestamp.
                if (unit is "hour" or "minute" or "second" && IsDate(call.Args[0], cx))
                {
                    cx.Note($"{name}() on a date takes 'year', 'month' or 'day'");
                    return string.Empty;
                }
                return cx.SqlServer
                    ? $"DATEADD({unit.ToUpperInvariant()}, {args[1]}, {args[0]})"
                    : $"({args[0]} + ({args[1]}) * INTERVAL '1 {unit}')";
            }
            case "date_diff":
            {
                if (!Arity(3) || Unit(call.Args[0], name, cx) is not { } unit) return string.Empty;
                if (cx.SqlServer) return $"DATEDIFF({unit.ToUpperInvariant()}, {args[1]}, {args[2]})";
                var (start, end) = (args[1], args[2]);
                return unit switch
                {
                    "day" => $"(({end})::date - ({start})::date)",
                    "year" => $"(EXTRACT(YEAR FROM {end}) - EXTRACT(YEAR FROM {start}))::int",
                    "month" => $"((EXTRACT(YEAR FROM {end}) - EXTRACT(YEAR FROM {start})) * 12 + EXTRACT(MONTH FROM {end}) - EXTRACT(MONTH FROM {start}))::int",
                    _ => $"FLOOR(EXTRACT(EPOCH FROM date_trunc('{unit}', {end}) - date_trunc('{unit}', {start})) / {(unit == "hour" ? 3600 : unit == "minute" ? 60 : 1)})::int",
                };
            }
            default:
                return string.Empty;
        }
    }

    /// <summary>Whether an argument is known to be a date: <c>today()</c>, or a field the caller says is a <c>date</c>.</summary>
    private static bool IsDate(List<Node> argument, RenderContext cx) => argument switch
    {
        [Call call] when call.Name.Equals("today", StringComparison.OrdinalIgnoreCase) => true,
        // Moved by whole units, a date stays a date; the first non-null of dates is a date.
        [Call call] when call.Name.Equals("date_add", StringComparison.OrdinalIgnoreCase) && call.Args.Count == 3
            => call.Args[2] is [Leaf { Token: { Kind: Kind.Text } u }] && u.Value.Length >= 2
               && u.Value[1..^1].ToLowerInvariant() is "year" or "month" or "day" && IsDate(call.Args[0], cx),
        [Call call] when call.Name.Equals("coalesce", StringComparison.OrdinalIgnoreCase) && call.Args.Count > 0
            => call.Args.All(a => IsDate(a, cx)),
        [Leaf { Token: { Kind: Kind.Name } t }] => cx.FieldType(t.Value) == "date",
        _ => false,
    };

    /// <summary>A unit argument: a string literal naming one of the units §10.7.4 lists.</summary>
    private static string? Unit(List<Node> argument, string function, RenderContext cx)
    {
        if (argument is [Leaf { Token: { Kind: Kind.Text } t }] && t.Value.Length >= 2
            && Units.Contains(t.Value[1..^1].ToLowerInvariant()))
            return t.Value[1..^1].ToLowerInvariant();
        cx.Note($"{function}() unit must be one of 'year', 'month', 'day', 'hour', 'minute', 'second'");
        return null;
    }

    /// <summary>
    /// Whether the top level is a comparison or a logical combination — what SQL Server cannot select. Tokens inside a
    /// <c>CASE … END</c> belong to its conditions, not to the value it produces.
    /// </summary>
    private static bool IsPredicate(List<Node> nodes)
    {
        if (nodes is [Group { Items: [var only] }]) return IsPredicate(only);
        var caseDepth = 0;
        foreach (var node in nodes)
        {
            if (node is not Leaf { Token: var t }) continue;
            if (t.Kind == Kind.Name && t.Value.Equals("case", StringComparison.OrdinalIgnoreCase)) { caseDepth++; continue; }
            if (t.Kind == Kind.Name && t.Value.Equals("end", StringComparison.OrdinalIgnoreCase)) { caseDepth--; continue; }
            if (caseDepth > 0) continue;
            if (t.Kind == Kind.Operator && t.Value is "=" or "<>" or "<" or ">" or "<=" or ">=") return true;
            if (t.Kind == Kind.Name && PredicateKeywords.Contains(t.Value)) return true;
        }
        return false;
    }
}
