using M3L.Native;
using MddBooster.Core.Ast;

namespace MddBooster.Core.Semantic;

/// <summary>What a view's <c>from</c> or <c>join</c> names: a model, or another view.</summary>
public enum ViewRelationKind
{
    Model,
    View,
}

/// <summary>A relation a view reads — a resolved model, or another view resolved before it.</summary>
public sealed record ViewRelation(string Name, ViewRelationKind Kind, ResolvedModel? Model, ResolvedView? View)
{
    /// <summary>Whether the relation has a column of that name: a model field, or a view's output column.</summary>
    public bool HasColumn(string name) => Kind == ViewRelationKind.Model
        ? Model!.Fields.Any(f => string.Equals(f.Name, name, StringComparison.Ordinal))
        : View!.Columns.Any(c => string.Equals(c.Name, name, StringComparison.Ordinal));
}

/// <summary><c>Relation.column</c> — a column of one of the relations a view reads.</summary>
public sealed record ViewColumnRef(ViewRelation Relation, string Column);

/// <summary>One <c>Left = Right</c> term of a join condition.</summary>
public sealed record ViewJoinTerm(ViewColumnRef Left, ViewColumnRef Right);

/// <summary><c>join: Target on A.x = B.y [AND …]</c>, the condition read as structure rather than as an expression.</summary>
public sealed record ViewJoin(ViewRelation Target, IReadOnlyList<ViewJoinTerm> On);

/// <summary>An output column of a view, by where its value comes from.</summary>
public abstract record ViewColumn(FieldNode Field)
{
    public string Name => Field.Name;
}

/// <summary><c>@from(Relation.column)</c> — a projected column.</summary>
public sealed record ViewFromColumn(FieldNode Field, ViewColumnRef Source) : ViewColumn(Field);

/// <summary><c>@rollup(Target.fk, aggregate)</c> — an aggregate over the rows of <paramref name="Target"/> that point at the view's <c>from</c> row.</summary>
public sealed record ViewRollupColumn(FieldNode Field, ResolvedModel Target, RollupDef Rollup) : ViewColumn(Field);

/// <summary>
/// <c>@computed("…")</c> and/or <c>@computed_raw(…, platform: …)</c> — the field's expressions; which one a dialect uses is
/// <see cref="ExpressionSelection"/>'s rule.
/// </summary>
public sealed record ViewComputedColumn(FieldNode Field, ComputedDef Computed) : ViewColumn(Field);

/// <summary>A standalone <c>::view</c> whose key-value <c>### Source</c> resolved completely.</summary>
public sealed class ResolvedView
{
    public required string Name { get; init; }
    public required ModelNode Source { get; init; }
    public required ViewRelation From { get; init; }
    public required IReadOnlyList<ViewJoin> Joins { get; init; }
    public string? Where { get; init; }
    /// <summary>The Source's <c>- where_raw:</c> filters, one per platform (M3L §10.7.5).</summary>
    public IReadOnlyList<PlatformExpression> WhereRaw { get; init; } = [];
    public required IReadOnlyList<ViewColumnRef> GroupBy { get; init; }
    public required IReadOnlyList<ViewColumn> Columns { get; init; }

    /// <summary>1 for a view over models only; one more than the deepest view it reads otherwise.</summary>
    public required int Depth { get; init; }

    /// <summary>Whether any part of the view is an expression string (<c>where</c>, <c>@computed</c>) rather than structure.</summary>
    public bool HasExpressions => Where is not null || WhereRaw.Count > 0 || Columns.Any(c => c is ViewComputedColumn);

    /// <summary>The filter <paramref name="dialect"/> uses (<see cref="ExpressionSelection"/>), or <c>null</c> for none.</summary>
    public SelectedExpression? WhereFor(SqlDialect dialect) => ExpressionSelection.Select(Where, WhereRaw, dialect);
}

/// <summary>The views that resolved, in dependency order (a view after every view it reads), and what was said about all of them.</summary>
public sealed record ViewResolution(IReadOnlyList<ResolvedView> Views, IReadOnlyList<SemanticDiagnostic> Diagnostics);

/// <summary>
/// Resolves standalone <c>::view</c> declarations against the models: every relation, every
/// <c>Relation.column</c> reference, every join condition — before any target renders one.
/// </summary>
/// <remarks>
/// <para>
/// A view is resolved or it is not generated; nothing is guessed. What the language defines and
/// the model gets wrong is an error (<c>MDD030</c> — an unknown relation or column, a join
/// condition that is not <c>A.x = B.y</c> terms joined by <c>AND</c>, a cycle between views). What
/// the language allows and this generator does not emit is a warning naming the view, which is
/// then left out: a SQL code block source (<c>MDD028</c>), and <c>MDD031</c> — a materialized
/// view, a <c>@lookup</c> column ("Lookup navigates, From projects": a view projects from its
/// source), a <c>group_by</c> entry naming an output column, a <c>@rollup</c> over a view source.
/// Nesting deeper than two views is legal and generated, with the warning the specification's
/// recommendation implies (<c>MDD032</c>).
/// </para>
/// <para>
/// A join condition is parsed as structure, not passed through as an expression, so it carries no
/// dialect: each side is a column of a relation the view reads, and rendering it is a matter of
/// naming. <c>where</c> and <c>@computed</c> stay strings — the language does not define their
/// dialect, so whether a target can render one is the target's decision.
/// </para>
/// </remarks>
public static class ViewResolver
{
    public static ViewResolution Resolve(IReadOnlyList<ModelNode> views, IReadOnlyList<ResolvedModel> models)
    {
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(models);

        var modelsByName = models.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var viewsByName = new Dictionary<string, ModelNode>(StringComparer.Ordinal);
        foreach (var view in views) viewsByName.TryAdd(view.Name, view);

        var diagnostics = new List<SemanticDiagnostic>();
        var resolved = new Dictionary<string, ResolvedView>(StringComparer.Ordinal);
        var failed = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<ResolvedView>();

        foreach (var view in views)
            Visit(view, new List<string>());

        return new ViewResolution(ordered, diagnostics);

        ResolvedView? Visit(ModelNode view, List<string> path)
        {
            if (resolved.TryGetValue(view.Name, out var done)) return done;
            if (failed.Contains(view.Name)) return null;
            if (path.Contains(view.Name, StringComparer.Ordinal))
            {
                diagnostics.Add(Error(view,
                    $"뷰가 서로를 읽는다: {string.Join(" → ", path.SkipWhile(p => p != view.Name).Append(view.Name))} — 순환은 정의되지 않는다."));
                failed.Add(view.Name);
                return null;
            }

            path.Add(view.Name);
            var result = ResolveOne(view, path);
            path.RemoveAt(path.Count - 1);

            if (result is null)
            {
                failed.Add(view.Name);
                return null;
            }
            resolved[view.Name] = result;
            ordered.Add(result);
            if (result.Depth > 2)
            {
                diagnostics.Add(new SemanticDiagnostic("MDD032",
                    $"뷰 '{view.Name}' 은(는) 뷰를 {result.Depth - 1}단 겹쳐 읽는다 — 명세는 2단까지를 권장한다(생성은 한다).",
                    view.Loc, SemanticSeverity.Warning));
            }
            return result;
        }

        ResolvedView? ResolveOne(ModelNode view, List<string> path)
        {
            var source = view.SourceDef;
            if (source?.RawSql is not null)
            {
                diagnostics.Add(new SemanticDiagnostic("MDD028",
                    $"뷰 '{view.Name}' 의 소스가 SQL 코드 블록이다 — 모델 이름을 방언 이름으로 바꾸려면 SQL 을 해석해야 해서 생성하지 않는다. "
                    + "`### Source` 를 from/join/where 항목으로 쓰면 생성된다.",
                    view.Loc, SemanticSeverity.Warning));
                return null;
            }
            if (view.Materialized == true)
            {
                diagnostics.Add(Unsupported(view, "구체화 뷰(@materialized)다 — 갱신 책임이 정해지지 않아 생성하지 않는다"));
                return null;
            }
            if (source?.From is not { Length: > 0 } fromName)
            {
                diagnostics.Add(Error(view, $"뷰 '{view.Name}' 에 `### Source` 의 from 이 없다."));
                return null;
            }

            var ok = true;
            var from = Relation(fromName, view, path, ref ok);
            if (from is null) return null;

            var relations = new Dictionary<string, ViewRelation>(StringComparer.Ordinal) { [from.Name] = from };
            var joins = new List<ViewJoin>();
            foreach (var join in source.Joins ?? [])
            {
                var target = Relation(join.Model, view, path, ref ok);
                if (target is null) continue;
                if (!relations.TryAdd(target.Name, target))
                {
                    diagnostics.Add(Error(view, $"뷰 '{view.Name}' 이(가) '{target.Name}' 을(를) 두 번 읽는다 — 같은 관계를 별칭 없이 두 번 조인할 수 없다."));
                    ok = false;
                    continue;
                }
                var terms = JoinTerms(join, view, relations, ref ok);
                if (terms is not null) joins.Add(new ViewJoin(target, terms));
            }
            if (!ok) return null;

            var groupBy = new List<ViewColumnRef>();
            foreach (var entry in source.GroupBy ?? [])
            {
                if (!entry.Contains('.'))
                {
                    diagnostics.Add(Unsupported(view, $"group_by 의 '{entry}' 가 출력 열을 가리킨다 — 출력 별칭으로 묶는 뷰는 아직 생성하지 않는다"));
                    return null;
                }
                var reference = ColumnRef(entry, view, relations, "group_by", ref ok);
                if (reference is not null) groupBy.Add(reference);
            }

            var columns = new List<ViewColumn>();
            foreach (var field in view.Fields)
            {
                var column = Column(field, view, from, relations, ref ok);
                if (column is null) continue;
                columns.Add(column);
            }
            if (!ok) return null;
            if (columns.Count == 0)
            {
                diagnostics.Add(Error(view, $"뷰 '{view.Name}' 에 열이 없다."));
                return null;
            }

            var depth = 1 + relations.Values.Where(r => r.Kind == ViewRelationKind.View).Select(r => r.View!.Depth).DefaultIfEmpty(0).Max();
            return new ResolvedView
            {
                Name = view.Name,
                Source = view,
                From = from,
                Joins = joins,
                Where = string.IsNullOrWhiteSpace(source.Where) ? null : source.Where,
                WhereRaw = source.WhereRaw ?? [],
                GroupBy = groupBy,
                Columns = columns,
                Depth = depth,
            };
        }

        ViewRelation? Relation(string name, ModelNode view, List<string> path, ref bool ok)
        {
            if (modelsByName.TryGetValue(name, out var model))
                return new ViewRelation(name, ViewRelationKind.Model, model, null);
            if (viewsByName.TryGetValue(name, out var other))
            {
                var inner = Visit(other, path);
                if (inner is not null) return new ViewRelation(name, ViewRelationKind.View, null, inner);
                // The inner view said why; this one cannot be generated without it.
                if (!failed.Contains(view.Name))
                    diagnostics.Add(Unsupported(view, $"읽는 뷰 '{name}' 을(를) 생성하지 않으므로"));
                ok = false;
                return null;
            }
            diagnostics.Add(Error(view, $"뷰 '{view.Name}' 이(가) 정의되지 않은 '{name}' 을(를) 읽는다."));
            ok = false;
            return null;
        }

        List<ViewJoinTerm>? JoinTerms(JoinDef join, ModelNode view, Dictionary<string, ViewRelation> relations, ref bool ok)
        {
            var terms = new List<ViewJoinTerm>();
            var parts = SplitOnAnd(join.On);
            if (parts.Count == 0)
            {
                diagnostics.Add(Error(view, $"뷰 '{view.Name}' 의 join '{join.Model}' 에 조건이 없다 — 명세는 명시 조건만 허용한다(`join: {join.Model} on A.x = B.y`)."));
                ok = false;
                return null;
            }
            foreach (var part in parts)
            {
                var sides = part.Split('=');
                if (sides.Length != 2 || !IsQualified(sides[0]) || !IsQualified(sides[1]))
                {
                    diagnostics.Add(Error(view,
                        $"뷰 '{view.Name}' 의 join 조건 '{join.On}' 을(를) 해석할 수 없다 — `Model.field = Model.field` 항을 AND 로 이은 것만 생성한다."));
                    ok = false;
                    return null;
                }
                var left = ColumnRef(sides[0].Trim(), view, relations, $"join '{join.Model}'", ref ok);
                var right = ColumnRef(sides[1].Trim(), view, relations, $"join '{join.Model}'", ref ok);
                if (left is null || right is null) return null;
                terms.Add(new ViewJoinTerm(left, right));
            }
            if (!terms.Any(t => t.Left.Relation.Name == join.Model || t.Right.Relation.Name == join.Model))
            {
                diagnostics.Add(Error(view, $"뷰 '{view.Name}' 의 join '{join.Model}' 조건이 '{join.Model}' 을(를) 쓰지 않는다."));
                ok = false;
                return null;
            }
            return terms;
        }

        ViewColumnRef? ColumnRef(string text, ModelNode view, Dictionary<string, ViewRelation> relations, string where, ref bool ok)
        {
            var dot = text.IndexOf('.');
            var relationName = dot > 0 ? text[..dot] : "";
            var columnName = dot > 0 ? text[(dot + 1)..] : text;
            if (!relations.TryGetValue(relationName, out var relation))
            {
                diagnostics.Add(Error(view,
                    $"뷰 '{view.Name}' 의 {where} 가 가리키는 '{text}' 의 '{relationName}' 은(는) 이 뷰의 from/join 이 아니다."));
                ok = false;
                return null;
            }
            if (!relation.HasColumn(columnName))
            {
                diagnostics.Add(Error(view, $"뷰 '{view.Name}' 의 {where} 가 가리키는 '{text}' — '{relationName}' 에 '{columnName}' 이(가) 없다."));
                ok = false;
                return null;
            }
            return new ViewColumnRef(relation, columnName);
        }

        ViewColumn? Column(FieldNode field, ModelNode view, ViewRelation from, Dictionary<string, ViewRelation> relations, ref bool ok)
        {
            if (FieldAttributes.FirstArg(field, "from") is { } fromArg)
            {
                var reference = ColumnRef(fromArg, view, relations, $"열 '{field.Name}' 의 @from", ref ok);
                if (reference is null) return null;
                // The column carries its source's value, so it has its source's shape: a list declared over a single
                // value (or the reverse) would type the read model against a column the view does not produce.
                var source = reference.Relation.Kind == ViewRelationKind.Model
                    ? reference.Relation.Model!.Fields.First(f => f.Name == reference.Column)
                    : reference.Relation.View!.Columns.First(c => c.Name == reference.Column).Field;
                if (source.Array != field.Array || (field.Array && source.ArrayItemNullable != field.ArrayItemNullable))
                {
                    diagnostics.Add(Error(view,
                        $"뷰 '{view.Name}' 의 열 '{field.Name}' 은(는) {Shape(field)} 로 선언됐지만 원천 '{fromArg}' 은(는) {Shape(source)} 다 — " +
                        "투영한 열은 원천의 모양을 따른다."));
                    ok = false;
                    return null;
                }
                return new ViewFromColumn(field, reference);
            }
            if (field.Array)
            {
                diagnostics.Add(Unsupported(view, $"열 '{field.Name}' 은(는) 배열로 선언된 {(field.Kind == FieldKind.Rollup ? "@rollup" : "계산")} 열이다 — 목록을 내는 집계 · 식은 생성하지 않는다"));
                ok = false;
                return null;
            }
            switch (field.Kind)
            {
                case FieldKind.Rollup when field.Rollup is { } rollup:
                    if (from.Kind != ViewRelationKind.Model)
                    {
                        diagnostics.Add(Unsupported(view, $"열 '{field.Name}' 의 @rollup 이 뷰 '{from.Name}' 위에 있다 — 상관 키가 모델 키여야 생성한다"));
                        ok = false;
                        return null;
                    }
                    if (!modelsByName.TryGetValue(rollup.Target, out var target))
                    {
                        diagnostics.Add(Error(view, $"뷰 '{view.Name}' 의 열 '{field.Name}' 이(가) 정의되지 않은 '{rollup.Target}' 을(를) 집계한다."));
                        ok = false;
                        return null;
                    }
                    if (!target.Fields.Any(f => f.Name == rollup.Fk))
                    {
                        diagnostics.Add(Error(view, $"뷰 '{view.Name}' 의 열 '{field.Name}' — '{rollup.Target}' 에 '{rollup.Fk}' 이(가) 없다."));
                        ok = false;
                        return null;
                    }
                    if (rollup.Field is { } aggregated && !target.Fields.Any(f => f.Name == aggregated))
                    {
                        diagnostics.Add(Error(view, $"뷰 '{view.Name}' 의 열 '{field.Name}' — '{rollup.Target}' 에 집계할 '{aggregated}' 이(가) 없다."));
                        ok = false;
                        return null;
                    }
                    return new ViewRollupColumn(field, target, rollup);
                case FieldKind.Computed when field.Computed is { } computed:
                    return new ViewComputedColumn(field, computed);
                case FieldKind.Lookup:
                    diagnostics.Add(Unsupported(view, $"열 '{field.Name}' 이(가) @lookup 이다 — 뷰는 소스에서 투영한다(@from)"));
                    ok = false;
                    return null;
                default:
                    diagnostics.Add(Error(view,
                        $"뷰 '{view.Name}' 의 열 '{field.Name}' 에 값의 원천이 없다 — @from(Model.field)·@rollup(…)·@computed(\"…\") 중 하나가 필요하다."));
                    ok = false;
                    return null;
            }
        }

        SemanticDiagnostic Unsupported(ModelNode view, string reason) => new("MDD031",
            $"뷰 '{view.Name}' 을(를) 생성하지 않는다 — {reason}.", view.Loc, SemanticSeverity.Warning);
    }

    private static SemanticDiagnostic Error(ModelNode view, string message) =>
        new("MDD030", message, view.Loc, SemanticSeverity.Error);

    private static string Shape(FieldNode field) => field.Array
        ? $"배열 `{field.Type}{(field.ArrayItemNullable ? "?" : "")}[]`"
        : $"값 하나 `{field.Type}`";

    private static bool IsQualified(string side)
    {
        var text = side.Trim();
        var dot = text.IndexOf('.');
        return dot > 0 && dot < text.Length - 1 && text.IndexOf('.', dot + 1) < 0
            && text.All(c => char.IsLetterOrDigit(c) || c is '_' or '.');
    }

    /// <summary>Splits a condition on the word AND (any case), keeping nothing else — OR, parentheses and literals are not structure.</summary>
    private static List<string> SplitOnAnd(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return [];
        var words = condition.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>();
        var current = new List<string>();
        foreach (var word in words)
        {
            if (string.Equals(word, "AND", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(string.Join(' ', current));
                current.Clear();
            }
            else current.Add(word);
        }
        parts.Add(string.Join(' ', current));
        return parts;
    }
}
