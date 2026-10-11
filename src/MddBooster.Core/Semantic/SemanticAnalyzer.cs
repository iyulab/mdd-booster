using M3L.Native;
using MddBooster.Core.Types;

namespace MddBooster.Core.Semantic;

/// <summary>
/// Lightweight semantic checks that run before code generation. These catch
/// the kind of cross-entity mistakes that M3L.Native's syntactic parser lets
/// through: dangling <c>@reference</c> targets, enum names that resolve to
/// neither a model nor an enum, rollup/lookup pointing at non-existent
/// entities. The analyzer never throws — it accumulates diagnostics and the
/// caller (typically <c>BuildCommand</c>) decides whether to abort.
/// </summary>
/// <remarks>
/// Current scope: field type checks, reference integrity, enum resolution,
/// lookup-path and rollup-target checks, binding checks, attribute-name typo
/// detection, and the extension rules for <c>::extend</c> blocks and models
/// that name a base. Deeper checks (rollup aggregate compatibility, circular
/// inheritance) are reserved for later so this stays a focused, fast pass.
/// </remarks>
public sealed class SemanticAnalyzer
{
    private readonly IReadOnlyList<ResolvedModel> _models;
    private readonly IReadOnlyList<EnumNode> _enums;
    private readonly HashSet<string> _modelNames;
    private readonly HashSet<string> _enumNames;

    public SemanticAnalyzer(IReadOnlyList<ResolvedModel> models, IReadOnlyList<EnumNode> enums)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _enums = enums ?? throw new ArgumentNullException(nameof(enums));
        _modelNames = new HashSet<string>(models.Select(m => m.Name), StringComparer.Ordinal);
        _enumNames = new HashSet<string>(enums.Select(e => e.Name), StringComparer.Ordinal);
    }

    public IReadOnlyList<SemanticDiagnostic> Analyze()
    {
        var diagnostics = new List<SemanticDiagnostic>();

        foreach (var model in _models)
        {
            foreach (var field in model.Fields)
            {
                CheckFieldType(model, field, diagnostics);
                CheckReferenceTarget(model, field, diagnostics);
                CheckSetNullOnRequiredKey(model, field, diagnostics);
                CheckLookupPath(model, field, diagnostics);
                CheckRollupTarget(model, field, diagnostics);
                CheckBinding(model, field, diagnostics);
                CheckAttributeTypos(model, field, diagnostics);
                CheckRowVersionUse(model, field, diagnostics);
            }
        }

        ExtensionRules.Check(_models, diagnostics);
        AspectRules.Check(_models, diagnostics);
        CheckManyToOneNavigationNames(diagnostics);
        CheckSearchableReferences(diagnostics);
        CheckFullTextIndexes(diagnostics);
        CheckDeclaredIndexNames(diagnostics);
        CheckUnreadIndexItems(diagnostics);

        return diagnostics;
    }

    /// <summary>
    /// <c>MDD043</c> (warning): an <c>### Indexes</c> item that names no columns — no <c>fields:</c>, an empty one, or
    /// a directive without columns. No index can be generated from it, and nothing else would say so.
    /// </summary>
    private void CheckUnreadIndexItems(List<SemanticDiagnostic> diagnostics)
    {
        foreach (var model in _models)
        {
            foreach (var name in SectionIndexParser.Unread(model))
            {
                diagnostics.Add(new SemanticDiagnostic(
                    "MDD043",
                    $"'{model.Name}' ### Indexes 항목 '{name}' 이(가) 열을 하나도 말하지 않는다 — 인덱스를 만들지 않는다. " +
                    "`- fields: [열, …]` 를 더하거나 `- {name}: @index(열, …)` 로 쓴다.",
                    model.Source.Loc,
                    SemanticSeverity.Warning));
            }
        }
    }

    /// <summary>
    /// <c>MDD042</c>: a name the model gives an index — an <c>### Indexes</c> item's name, or a directive's
    /// <c>name: "…"</c> — that cannot be the database object's name: not a plain identifier (it is written into the
    /// generated SQL as it is), or the same as another declared name in the build (case-insensitively: SQL Server's
    /// constraint names and PostgreSQL's index names are unique across the schema, not within one table).
    /// </summary>
    private void CheckDeclaredIndexNames(List<SemanticDiagnostic> diagnostics)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in _models)
        {
            foreach (var entry in SectionIndexParser.Parse(model))
            {
                if (entry.Name is not { } name) continue;

                if (!PlainIdentifier.IsMatch(name))
                {
                    diagnostics.Add(new SemanticDiagnostic(
                        "MDD042",
                        $"'{model.Name}' 인덱스 이름 \"{name}\" 은(는) 데이터베이스 이름으로 쓸 수 없다 — 영문자·숫자·밑줄만, " +
                        "숫자로 시작하지 않게.",
                        model.Source.Loc));
                }
                else if (seen.TryGetValue(name, out var first))
                {
                    diagnostics.Add(new SemanticDiagnostic(
                        "MDD042",
                        $"'{model.Name}' 인덱스 이름 \"{name}\" 을(를) '{first}' 이(가) 이미 쓴다 — 인덱스·제약 이름은 " +
                        "스키마 안에서 유일해야 한다.",
                        model.Source.Loc));
                }
                else
                {
                    seen[name] = model.Name;
                }
            }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex PlainIdentifier =
        new("^[A-Za-z_][A-Za-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// <c>MDD041</c> (warning): an index declared <c>fulltext: true</c>. Neither SQL target generates a full-text
    /// index — SQL Server's needs a catalog and a key index, PostgreSQL's a text-search expression — so the entry is
    /// emitted as the ordinary index on the same columns. Said out loud, because an ordinary index does not answer the
    /// word searches the declaration was written for.
    /// </summary>
    private void CheckFullTextIndexes(List<SemanticDiagnostic> diagnostics)
    {
        foreach (var model in _models)
        {
            foreach (var entry in SectionIndexParser.Parse(model).Where(e => e.FullText))
            {
                diagnostics.Add(new SemanticDiagnostic(
                    "MDD041",
                    $"'{model.Name}' @index({string.Join(", ", entry.Columns)}, fulltext: true): 이 생성기는 전문(full-text) " +
                    "인덱스를 만들지 않는다 — 같은 열의 일반 인덱스로 낸다. 단어 검색이 필요하면 전문 인덱스는 " +
                    "생성물 밖(배포 스크립트)에서 만든다.",
                    model.Source.Loc,
                    SemanticSeverity.Warning));
            }
        }
    }

    /// <summary>
    /// <c>MDD034</c> (warning): a reference key declared <c>@searchable</c> whose read type has no
    /// navigation for it. The declaration lands on the navigation when there is one
    /// (<see cref="Generation.SearchableReferences"/>); without one it stays on the key, which is not
    /// text and matches no search term — and, being a declaration, still narrows the type's search to
    /// the declared properties. Search cannot follow that reference, and that is said here rather than
    /// discovered as an empty result.
    /// </summary>
    private void CheckSearchableReferences(List<SemanticDiagnostic> diagnostics)
    {
        var oneToOne = Generation.OneToOneRelationships.Describe(_models);
        var manyToOne = Generation.ManyToOneNavigations.Describe(_models, oneToOne);
        var known = _models.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var model in _models)
        {
            var navigated = Generation.SearchableReferences.ReadNavigatedKeys(model.Name, oneToOne, manyToOne);
            foreach (var field in model.Fields)
            {
                if (field.Kind != FieldKind.Stored || !Generation.SearchableReferences.IsSearchableReference(field)) continue;
                // Not on the read type at all, or pointing outside the set (reported where references are checked).
                if (Generation.EntitySurface.IsFieldInternal(field) || Ast.FieldAttributes.Has(field, "pk")) continue;
                var target = Ast.FieldAttributes.FirstArg(field, "reference")!;
                if (!known.Contains(target) || navigated.Contains(field.Name)) continue;

                var why = Generation.ManyToOneNavigations.IsNavigableKey(field)
                    ? "navigation 이 이름 충돌로 생성되지 않았다(MDD027)"
                    : "키 이름이 '_id' 로 끝나지 않아 navigation 이 없다";
                diagnostics.Add(new SemanticDiagnostic(
                    "MDD034",
                    $"'{model.Name}.{field.Name}' @searchable @reference({target}): 읽기 타입에 이 키의 navigation 이 없다({why}) — " +
                    "$search 가 참조 대상의 텍스트를 따라가지 못한다. 선언은 키에 남지만 키는 텍스트가 아니라 어떤 낱말에도 매칭하지 않는다.",
                    field.Loc,
                    SemanticSeverity.Warning));
            }
        }
    }

    /// <summary>
    /// <c>MDD027</c> (warning): a reference key whose read-type navigation would take a name the read
    /// type already uses — a declared field (typically a lookup spelled like the key without
    /// <c>_id</c>) or a one-to-one navigation. The declared member keeps the name and the navigation is
    /// not generated, so <c>$expand</c> cannot follow that key; saying so is the difference between a
    /// missing navigation and a silently missing one.
    /// </summary>
    private void CheckManyToOneNavigationNames(List<SemanticDiagnostic> diagnostics)
    {
        var oneToOne = Generation.OneToOneRelationships.Describe(_models);
        foreach (var n in Generation.ManyToOneNavigations.Describe(_models, oneToOne))
        {
            if (n.CollidesWith is null) continue;
            var field = _models.First(m => m.Name == n.DependentModel).Fields.First(f => f.Name == n.ForeignKeyField);
            diagnostics.Add(new SemanticDiagnostic(
                "MDD027",
                $"'{n.DependentModel}.{n.ForeignKeyField}' @reference({n.TargetModel}): 읽기 타입 navigation '{n.Name}' 이 " +
                $"이미 있는 멤버({n.CollidesWith})와 이름이 같아 생성하지 않는다 — 이 키로는 $expand 할 수 없다. " +
                "그 멤버의 이름을 바꾸면 navigation 이 생긴다.",
                field.Loc,
                SemanticSeverity.Warning));
        }
    }

    private void CheckLookupPath(ResolvedModel model, FieldNode field, List<SemanticDiagnostic> diagnostics)
    {
        if (field.Kind != FieldKind.Lookup || field.Lookup is null) return;
        var path = field.Lookup.Path;
        if (string.IsNullOrWhiteSpace(path)) return;

        var walk = LookupPath.Walk(model, path, _models);
        var at = walk.FailedAt?.Name;
        var segment = walk.FailedSegment;
        // A failure past the first hop names the model it happened on — in a two-hop path the
        // second key is looked up on the model the first one references, not on this one.
        var where = walk.Hops.Count == 0 ? "동일 모델" : $"'{at}'";

        switch (walk.Failure)
        {
            case LookupPathFailure.Malformed:
                diagnostics.Add(new SemanticDiagnostic(
                    "MDD003",
                    $"'{model.Name}.{field.Name}' @lookup 경로 '{path}'가 'fk.column' 형태가 아닙니다.",
                    field.Loc));
                break;
            case LookupPathFailure.MissingFk:
                diagnostics.Add(new SemanticDiagnostic(
                    "MDD004",
                    $"'{model.Name}.{field.Name}' @lookup({path}): {where}에 FK 필드 '{segment}'가 존재하지 않습니다.",
                    field.Loc));
                break;
            case LookupPathFailure.NoReference:
                diagnostics.Add(new SemanticDiagnostic(
                    "MDD005",
                    $"'{model.Name}.{field.Name}' @lookup({path}): FK 필드 '{(walk.Hops.Count == 0 ? segment : $"{at}.{segment}")}'에 @reference(Target) 속성이 없습니다.",
                    field.Loc));
                break;
            case LookupPathFailure.MissingColumn:
                diagnostics.Add(new SemanticDiagnostic(
                    "MDD006",
                    $"'{model.Name}.{field.Name}' @lookup({path}): 대상 엔티티 '{at}'에 필드 '{segment}'가 존재하지 않습니다.",
                    field.Loc));
                break;
            // UnknownTarget: the reference itself is reported where references are checked.
        }
    }

    /// <summary>
    /// A row version that cannot do its job. <c>MDD025</c> (warning): marked <c>@internal</c>, it
    /// drops out of the read type — the surface the ETag is built from — so the set stops offering
    /// conditional writes at all. <c>MDD026</c>: a lookup reading another model's row version; the
    /// value identifies a version of that row and says nothing about this one, and on PostgreSQL the
    /// version is a system column a join cannot read under the field's name.
    /// </summary>
    private void CheckRowVersionUse(ResolvedModel model, FieldNode field, List<SemanticDiagnostic> diagnostics)
    {
        if (field.Kind == FieldKind.Stored && M3lPrimitives.IsRowVersion(field)
            && MddBooster.Core.Generation.EntitySurface.IsFieldInternal(field))
        {
            diagnostics.Add(new SemanticDiagnostic(
                "MDD025",
                $"'{model.Name}.{field.Name}': @internal 행 버전(rowversion)은 읽기 타입에서 빠져 이 셋이 ETag 를 내지 않는다 — " +
                "If-Match 조건부 쓰기가 사라진다. 행 버전을 노출하거나, 동시성 검사를 원치 않으면 필드를 지우세요.",
                field.Loc,
                SemanticSeverity.Warning));
        }

        if (field.Kind == FieldKind.Lookup
            && LookupTarget.Resolve(model, field, _models) is { } target
            && M3lPrimitives.IsRowVersion(target))
        {
            var owner = LookupPath.Walk(model, field.Lookup!.Path, _models).Target?.Name ?? "?";
            diagnostics.Add(new SemanticDiagnostic(
                "MDD026",
                $"'{model.Name}.{field.Name}' @lookup({field.Lookup.Path}): '{owner}.{target.Name}' 는 그 행의 버전(rowversion)이다 — " +
                "다른 행이 읽어 쓸 값이 아니다(PostgreSQL 에서는 조인으로 읽을 열도 없다). 그 행의 동시성은 그 행의 ETag 로 다루세요.",
                field.Loc));
        }
    }

    private void CheckRollupTarget(ResolvedModel model, FieldNode field, List<SemanticDiagnostic> diagnostics)
    {
        if (field.Kind != FieldKind.Rollup || field.Rollup is null) return;
        var target = field.Rollup.Target;
        var fk = field.Rollup.Fk;
        if (string.IsNullOrEmpty(target))
        {
            diagnostics.Add(new SemanticDiagnostic(
                "MDD007",
                $"'{model.Name}.{field.Name}' @rollup에 Target이 없습니다.",
                field.Loc));
            return;
        }
        if (!_modelNames.Contains(target))
        {
            diagnostics.Add(new SemanticDiagnostic(
                "MDD008",
                $"'{model.Name}.{field.Name}' @rollup({target}.{fk}): 대상 엔티티 '{target}'가 존재하지 않습니다.",
                field.Loc));
            return;
        }
        if (!string.IsNullOrEmpty(fk))
        {
            var targetModel = _models.First(m => m.Name == target);
            if (!targetModel.Fields.Any(f => f.Name == fk))
            {
                diagnostics.Add(new SemanticDiagnostic(
                    "MDD009",
                    $"'{model.Name}.{field.Name}' @rollup({target}.{fk}): 대상 엔티티에 FK 필드 '{fk}'가 존재하지 않습니다.",
                    field.Loc));
            }
        }
    }

    private void CheckBinding(ResolvedModel model, FieldNode field, List<SemanticDiagnostic> diagnostics)
    {
        if (field.Binding is null) return;

        var b = field.Binding;
        if (!_modelNames.Contains(b.Entity))
        {
            diagnostics.Add(new SemanticDiagnostic(
                "MDD010",
                $"'{model.Name}.{field.Name}' # {b.Entity}.{b.Column}: 대상 엔티티 '{b.Entity}'가 존재하지 않습니다.",
                field.Loc));
            return;
        }

        var targetModel = _models.First(m => m.Name == b.Entity);
        // Entity name: case-sensitive (m3l entity names are always PascalCase, stored as-is).
        // Column name: case-insensitive (binding syntax uses PascalCase "Key" but m3l AST stores snake_case "key").
        if (!targetModel.Fields.Any(f =>
                string.Equals(f.Name, b.Column, StringComparison.OrdinalIgnoreCase) &&
                f.Kind == FieldKind.Stored))
        {
            diagnostics.Add(new SemanticDiagnostic(
                "MDD011",
                $"'{model.Name}.{field.Name}' # {b.Entity}.{b.Column}: 대상 엔티티에 저장 필드 '{b.Column}'가 없습니다.",
                field.Loc));
        }
    }

    private void CheckFieldType(ResolvedModel model, FieldNode field, List<SemanticDiagnostic> diagnostics)
    {
        var type = field.Type;
        if (string.IsNullOrWhiteSpace(type)) return;
        if (M3lPrimitives.Contains(type)) return;
        if (_enumNames.Contains(type)) return;
        // Some derived fields (lookup/rollup) may carry the source model's
        // type name — that's still a valid enum/primitive match above, so
        // anything reaching here is unresolved.
        if (field.Kind is FieldKind.Lookup or FieldKind.Rollup or FieldKind.Computed)
        {
            // Derived fields use primitive types in practice (string, integer, decimal);
            // a foreign model name would be an unusual pattern we don't currently support.
            if (_modelNames.Contains(type)) return;
        }

        diagnostics.Add(new SemanticDiagnostic(
            "MDD001",
            $"'{model.Name}.{field.Name}' 필드의 타입 '{type}'을 해석할 수 없습니다. primitive/enum/model 중 어느 것과도 매칭되지 않습니다.",
            field.Loc));
    }

    /// <summary>
    /// <c>MDD023</c> — <c>@reference(X)?</c> (SET NULL) on a key that cannot hold NULL. The
    /// database refuses the constraint (SQL Server: at creation; PostgreSQL: on the first delete),
    /// so the build stops here instead (M3L §8.4.1: SET NULL "requires nullable field").
    /// </summary>
    private static void CheckSetNullOnRequiredKey(ResolvedModel model, FieldNode field, List<SemanticDiagnostic> diagnostics)
    {
        if (field.Nullable || Ast.FieldAttributes.Find(field, "reference")?.Cascade != "?") return;
        diagnostics.Add(new SemanticDiagnostic(
            "MDD023",
            $"'{model.Name}.{field.Name}' 의 @reference(…)? 는 삭제 시 NULL 로 두라는 뜻인데 이 키는 NULL 을 허용하지 않습니다. " +
            "필드를 nullable(`identifier?`)로 하거나 다른 동작(`!` NO ACTION · `!!` RESTRICT)을 쓰십시오.",
            field.Loc));
    }

    private void CheckReferenceTarget(ResolvedModel model, FieldNode field, List<SemanticDiagnostic> diagnostics)
    {
        var refAttr = Ast.FieldAttributes.Find(field, "reference");
        if (refAttr?.Args is null || refAttr.Args.Count == 0) return;

        var target = refAttr.Args[0].ValueKind == System.Text.Json.JsonValueKind.String
            ? refAttr.Args[0].GetString()
            : refAttr.Args[0].GetRawText();

        if (string.IsNullOrEmpty(target)) return;
        if (_modelNames.Contains(target)) return;

        // A model name never contains a colon, so a target that does is naming a
        // scheme (M3L §5.2, external://…) — specified by the language, not read by
        // this generator. Reporting that as a missing entity sends the reader
        // hunting for a typo in a name that was never meant to be one.
        var colon = target.IndexOf(':');
        if (colon > 0)
        {
            var scheme = target[..colon];
            diagnostics.Add(new SemanticDiagnostic(
                "MDD012",
                $"'{model.Name}.{field.Name}' 필드의 @reference 대상이 '{scheme}' 스킴을 사용합니다. " +
                "이 생성기는 스킴이 붙은 참조를 해석하지 않습니다 — 대상은 이 빌드가 로드한 모델 이름이어야 합니다. " +
                "이 빌드가 소유하지 않은 테이블을 가리키려면 @reference 없이 컬럼만 선언하십시오(외래 키·내비게이션은 생성되지 않습니다).",
                field.Loc));
            return;
        }

        diagnostics.Add(new SemanticDiagnostic(
            "MDD002",
            $"'{model.Name}.{field.Name}' 필드의 @reference({target}) 대상 엔티티가 존재하지 않습니다.",
            field.Loc));
    }

    /// <summary>
    /// MDD018 — 오타 의심 속성 경고. 스펙 §10.8은 카탈로그 밖 속성을 custom으로
    /// 허용하므로, 알려진 어휘(<see cref="Ast.FieldAttributes.KnownNames"/>)와
    /// 편집거리 ≤2로 가까운 이름만 Warning으로 보고한다 (합법 custom은 침묵).
    /// </summary>
    private static void CheckAttributeTypos(ResolvedModel model, FieldNode field, List<SemanticDiagnostic> diagnostics)
    {
        foreach (var attr in field.Attributes)
        {
            var name = attr.Name;
            if (string.IsNullOrEmpty(name) || Ast.FieldAttributes.KnownNames.Contains(name)) continue;

            var suggestion = Naming.EditDistance.Nearest(name, Ast.FieldAttributes.KnownNames, maxDistance: 2);
            if (suggestion is null) continue;

            diagnostics.Add(new SemanticDiagnostic(
                "MDD018",
                $"'{model.Name}.{field.Name}'의 속성 '@{name}'은 알려진 속성이 아닙니다 — '@{suggestion}'의 오타일 수 있습니다. " +
                "(의도한 custom 속성이라면 무시해도 됩니다)",
                field.Loc,
                SemanticSeverity.Warning));
        }
    }
}

public enum SemanticSeverity
{
    Warning,
    Error,
}

public sealed record SemanticDiagnostic(
    string Code,
    string Message,
    SourceLocation? Location,
    SemanticSeverity Severity = SemanticSeverity.Error)
{
    public string Format()
    {
        var loc = Location is null
            ? string.Empty
            : $" {Location.File}:{Location.Line}:{Location.Col}";
        return $"[{Code}]{loc} {Message}";
    }
}
