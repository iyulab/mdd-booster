using M3L.Native;
using MddBooster.Core.Types;
using MddBooster.Core.Naming;

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
    private readonly IReadOnlyList<SqlDialect> _dialects;

    /// <param name="dialects">
    /// The SQL dialects this build writes object names in — a Sql target's, and a Model target's (it repeats index
    /// names in <c>HasDatabaseName</c>). Name checks that depend on a dialect's spelling or limits run only for these:
    /// a collision in output nobody produces is a false alarm. <see langword="null"/> checks both.
    /// </param>
    public SemanticAnalyzer(IReadOnlyList<ResolvedModel> models, IReadOnlyList<EnumNode> enums,
        IReadOnlyCollection<SqlDialect>? dialects = null)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _enums = enums ?? throw new ArgumentNullException(nameof(enums));
        _dialects = dialects is null ? [SqlDialect.SqlServer, SqlDialect.PostgreSql] : dialects.Distinct().ToList();
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
                CheckSystemDeclaration(model, field, diagnostics);
                CheckLegacyLabel(model, field, diagnostics);
                CheckIndexedRollup(model, field, diagnostics);
            }
        }

        ExtensionRules.Check(_models, diagnostics);
        AspectRules.Check(_models, diagnostics);
        CheckManyToOneNavigationNames(diagnostics);
        CheckRelationDeclarations(diagnostics);
        CheckUnnamedNavigations(diagnostics);
        CheckSearchableReferences(diagnostics);
        CheckFullTextIndexes(diagnostics);
        CheckDeclaredIndexNames(diagnostics);
        CheckGeneratedIndexNames(diagnostics);
        CheckUnreadIndexItems(diagnostics);

        return diagnostics;
    }

    /// <summary>
    /// <c>MDD052</c> (warning): a rollup asks to be stored ahead of reads (<c>@indexed</c> — an indexed view on SQL
    /// Server, a materialized view on PostgreSQL), and this generator does not produce one: the rollup is computed when
    /// the view is read. An indexed view cannot hold the correlated subquery a rollup is, so binding the read view to its
    /// schema was never a step toward one — and nothing else would say the attribute does nothing.
    /// </summary>
    private static void CheckIndexedRollup(ResolvedModel model, FieldNode field, List<SemanticDiagnostic> diagnostics)
    {
        if (field.Kind != FieldKind.Rollup || !Ast.FieldAttributes.Has(field, "indexed")) return;
        diagnostics.Add(new SemanticDiagnostic(
            "MDD052",
            $"'{model.Name}.{field.Name}' 의 `@indexed` 는 생성하지 않는다 — 이 롤업은 뷰를 읽을 때 계산된다(SQL Server 색인 뷰 · " +
            "PostgreSQL 구체화 뷰 없음). 색인 뷰는 롤업의 상관 하위 쿼리를 담을 수 없다.",
            field.Loc,
            SemanticSeverity.Warning));
    }

    /// <summary>
    /// <c>MDD047</c> (warning): a field names its display label with <c>@label(…)</c>, this generator's own attribute,
    /// where the specification has a form for it — <c>- name(Label): type</c>. Still read for now; the label in
    /// parentheses wins when both are written.
    /// </summary>
    private static void CheckLegacyLabel(ResolvedModel model, FieldNode field, List<SemanticDiagnostic> diagnostics)
    {
        if (Ast.FieldAttributes.FirstArg(field, "label") is not { } legacy) return;
        var conflict = !string.IsNullOrWhiteSpace(field.Label) && field.Label != legacy
            ? $" 괄호 라벨 '{field.Label}' 이 함께 있어 그것을 쓴다 — '{legacy}' 는 쓰이지 않는다."
            : "";
        diagnostics.Add(new SemanticDiagnostic(
            "MDD047",
            $"'{model.Name}.{field.Name}' 의 `@label(…)` 은 명세에 없는 이 생성기의 옛 어휘다 — 명세의 표시 라벨 " +
            $"`- {field.Name}({field.Label ?? legacy}): …` 로 옮기십시오. 다음 마이너 판부터 `@label` 은 읽지 않는다.{conflict}",
            field.Loc,
            SemanticSeverity.Warning));
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
    /// constraint names and PostgreSQL's index names are unique across the schema, not within one table), or a name a
    /// generator gives another index in a dialect this build writes. One longer than a built dialect holds is
    /// <c>MDD044</c>.
    /// </summary>
    private void CheckDeclaredIndexNames(List<SemanticDiagnostic> diagnostics)
    {
        var generated = _dialects.ToDictionary(d => d, d => GeneratedIndexNames(d)
            .GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Owner, StringComparer.OrdinalIgnoreCase));
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
                else if (_dialects.Contains(SqlDialect.PostgreSql) && Naming.PostgresIdentifiers.Check(name) is { } pgViolation)
                {
                    // The PostgreSQL generator writes a declared name unquoted and refuses, at generation time, one it
                    // cannot write that way. The same gate is asked here so the build says it with its diagnostics.
                    diagnostics.Add(new SemanticDiagnostic(
                        "MDD042",
                        $"'{model.Name}' 인덱스 이름을 PostgreSQL 이 쓸 수 없다 — {pgViolation}.",
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
                else if (_dialects.Select(d => generated[d].GetValueOrDefault(name)).FirstOrDefault(o => o is not null) is { } owner)
                {
                    diagnostics.Add(new SemanticDiagnostic(
                        "MDD042",
                        $"'{model.Name}' 인덱스 이름 \"{name}\" 은(는) '{owner}' 의 다른 인덱스가 생성되는 이름이다 — 인덱스·제약 " +
                        "이름은 스키마 안에서 유일해야 한다. 다른 이름을 준다.",
                        model.Source.Loc));
                }
                else
                {
                    seen[name] = model.Name;
                    // PostgreSQL's limit is part of its gate above; SQL Server's is the one left to say.
                    if (_dialects.Contains(SqlDialect.SqlServer) && Limit(SqlDialect.SqlServer, name) is { } limit)
                        diagnostics.Add(new SemanticDiagnostic(
                            "MDD044",
                            $"'{model.Name}' 인덱스 이름 \"{name}\" 은(는) {limit} — 더 짧은 이름을 준다.",
                            model.Source.Loc));
                }
            }
        }
    }

    /// <summary>
    /// The names generated for indexes the model does not name, in each dialect this build writes:
    /// <c>MDD045</c> when two coincide — PostgreSQL joins columns with the underscore a column name may itself contain,
    /// so <c>(a, b)</c> and <c>a_b</c> are both <c>ix_part_a_b</c>, and index names are schema-wide — and <c>MDD044</c>
    /// when one is longer than the dialect holds (SQL Server refuses the object; PostgreSQL would cut the name short and
    /// the EF model's <c>HasDatabaseName</c> would name an index that does not exist). Both are fixed the same way:
    /// name the index in <c>### Indexes</c>. Truncating or suffixing here would invent a name the model never said.
    /// </summary>
    private void CheckGeneratedIndexNames(List<SemanticDiagnostic> diagnostics)
    {
        foreach (var dialect in _dialects)
        {
            foreach (var group in GeneratedIndexNames(dialect).GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();
                var at = _models.First(m => m.Name == first.Owner).Source.Loc;
                if (group.Count() > 1)
                {
                    diagnostics.Add(new SemanticDiagnostic(
                        "MDD045",
                        $"{dialect.PlatformName()}: 이름 없는 인덱스 {group.Count()}개가 같은 이름 \"{first.Name}\" 으로 생성된다 " +
                        $"({string.Join(", ", group.Select(n => $"'{n.Owner}' ({string.Join(", ", n.Columns)})"))}) — 인덱스 이름은 " +
                        "스키마 안에서 유일해야 한다. `### Indexes` 에서 한쪽에 이름을 준다.",
                        at));
                }
                if (Limit(dialect, first.Name) is { } limit)
                {
                    diagnostics.Add(new SemanticDiagnostic(
                        "MDD044",
                        $"'{first.Owner}' ({string.Join(", ", first.Columns)}) 의 생성 인덱스 이름 \"{first.Name}\" 은(는) {limit} — " +
                        "`### Indexes` 에서 더 짧은 이름을 준다.",
                        at));
                }
            }
        }
    }

    private static string? Limit(SqlDialect dialect, string name) => dialect switch
    {
        SqlDialect.SqlServer when name.Length > 128 =>
            $"{name.Length}자로 SQL Server 식별자 한계 128자를 넘는다",
        SqlDialect.PostgreSql when System.Text.Encoding.UTF8.GetByteCount(name) > 63 =>
            $"{System.Text.Encoding.UTF8.GetByteCount(name)}바이트로 PostgreSQL 식별자 한계 63바이트를 넘는다",
        _ => null,
    };

    /// <summary>
    /// The names the generators give the indexes and unique constraints the model does not name — field-level
    /// <c>@unique</c>/<c>@index</c> and unnamed <c>### Indexes</c> entries — in <paramref name="dialect"/>'s form, one per index, with the model each
    /// belongs to. A declared name is checked against these: a name the model gives one index that a generator gives
    /// another collides at deployment. Primary-key, foreign-key and check constraints are left out — whether a
    /// generator writes them depends on the target's options and the model's relations, which this pass does not see;
    /// a declared name of that shape is rare and the deployment reports it.
    /// </summary>
    private List<(string Name, string Owner, IReadOnlyList<string> Columns)> GeneratedIndexNames(SqlDialect dialect)
    {
        var postgres = dialect == SqlDialect.PostgreSql;
        var names = new List<(string Name, string Owner, IReadOnlyList<string> Columns)>();
        void Add(ResolvedModel model, Naming.GeneratedObjectNames.Kind kind, IReadOnlyList<string> fields)
        {
            var name = postgres
                ? Naming.GeneratedObjectNames.For(true, kind, Naming.PostgresIdentifiers.ToSnakeCase(model.Name), fields)
                : Naming.GeneratedObjectNames.For(false, kind, model.Name, fields.Select(Naming.NameCasing.ToPascalCase));
            names.Add((name, model.Name, fields));
        }

        foreach (var model in _models)
        {
            foreach (var field in model.Fields.Where(f => f.Kind == FieldKind.Stored && !Ast.FieldAttributes.Has(f, "pk")))
            {
                if (Ast.FieldAttributes.Has(field, "unique")) Add(model, Naming.GeneratedObjectNames.Kind.Unique, [field.Name]);
                else if (Ast.FieldAttributes.Has(field, "index")) Add(model, Naming.GeneratedObjectNames.Kind.Index, [field.Name]);
            }
            foreach (var entry in SectionIndexParser.Parse(model).Where(e => e.Name is null))
            {
                Add(model, entry.IsUnique ? Naming.GeneratedObjectNames.Kind.Unique : Naming.GeneratedObjectNames.Kind.Index, entry.Columns);
            }
        }
        return names;
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

                var why = Generation.ManyToOneNavigations.IsNavigableKey(model.Source, field)
                    ? "navigation 이 이름 충돌로 생성되지 않았다(MDD027)"
                    : "navigation 이름이 없다(MDD049)";
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
    /// <c>MDD050</c>: a <c>### Relations</c> entry (or <c>@relation</c>) this generator cannot name a navigation from —
    /// a reverse entry without its key, or a declared name that is already a member. An entry that contradicts the
    /// model's <c>@reference</c> keys is the parser's error (<c>M3L-E010</c>) and stops the build before this runs.
    /// </summary>
    private void CheckRelationDeclarations(List<SemanticDiagnostic> diagnostics)
    {
        foreach (var model in _models)
        {
            foreach (var (entry, problem) in RelationDeclarations.Problems(model))
                diagnostics.Add(new SemanticDiagnostic(
                    "MDD050",
                    $"'{model.Name}' ### Relations '{entry.Raw}': {problem}.",
                    entry.Loc ?? model.Source.Loc));

            // A declared forward name that is a member already: the generated class would not compile.
            foreach (var field in model.Fields.Where(f => f.Kind == FieldKind.Stored))
            {
                if (RelationDeclarations.ForwardName(model.Source, field) is not { } name) continue;
                if (model.Fields.FirstOrDefault(f => NameCasing.ToPascalCase(f.Name) == name) is not { } clash) continue;
                diagnostics.Add(new SemanticDiagnostic(
                    "MDD050",
                    $"'{model.Name}.{field.Name}': ### Relations 가 navigation 이름으로 '{name}' 을 주었는데 필드 '{clash.Name}' 의 속성 이름과 같다 — 다른 이름을 준다.",
                    field.Loc));
            }
        }
    }

    /// <summary>
    /// <c>MDD049</c> (warning): a reference key whose navigation has no name, so none is generated — a key not named
    /// <c>xxx_id</c> (its own name is the key property's) and a self-referencing one-to-one (its reverse side would land
    /// on the type it starts from, with nothing to derive a name from). The generator does not invent a name; without
    /// the navigation, <c>$expand</c> cannot follow the key and EF cannot order the inserts of a parent and child saved
    /// together. <c>### Relations</c> supplies the name.
    /// </summary>
    private void CheckUnnamedNavigations(List<SemanticDiagnostic> diagnostics)
    {
        var known = _models.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var model in _models)
        {
            var aspectKey = model.AspectBase is { } b ? AspectNaming.KeyFieldName(b) : null;
            foreach (var field in model.Fields.Where(f => f.Kind == FieldKind.Stored && f.Name != aspectKey))
            {
                if (Ast.FieldAttributes.FirstArg(field, "reference") is not { } target || !known.Contains(target)) continue;
                if (Generation.ManyToOneNavigations.NavigationName(model.Source, field) is null)
                {
                    diagnostics.Add(new SemanticDiagnostic(
                        "MDD049",
                        $"'{model.Name}.{field.Name}' @reference({target}): 키 이름이 '_id' 로 끝나지 않아 navigation 이름이 없다 — " +
                        "navigation 을 생성하지 않는다($expand 불가, EF 는 부모 · 자식을 함께 저장할 때 순서를 추론하지 못한다). " +
                        $"`### Relations` 에 `- >이름` 과 `- from: {field.Name}` 으로 이름을 준다.",
                        field.Loc,
                        SemanticSeverity.Warning));
                    continue;
                }
                var oneToOne = Ast.FieldAttributes.Has(field, "unique") || Ast.FieldAttributes.Has(field, "pk");
                if (oneToOne && target == model.Name
                    && RelationDeclarations.ReverseName(model.Source, model.Name, field.Name) is null)
                    diagnostics.Add(new SemanticDiagnostic(
                        "MDD049",
                        $"'{model.Name}.{field.Name}' @reference({target}) @unique: 자기참조 1:1 의 역방향에 이름이 없다 — 이 관계의 navigation 을 " +
                        "생성하지 않는다. `### Relations` 에 `- <이름` 과 `- target: " + model.Name + "." + field.Name + "` 으로 역방향 이름을 준다.",
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

        // The same rule for a declared reverse collection, on the side that declares it.
        var manyToOne = Generation.ManyToOneNavigations.Describe(_models, oneToOne);
        foreach (var c in Generation.ReverseCollections.Describe(_models, oneToOne, manyToOne))
        {
            if (c.CollidesWith is null) continue;
            var principal = _models.First(m => m.Name == c.PrincipalModel);
            var entry = RelationDeclarations.Of(principal.Source)
                .First(e => e is { Direction: "from" } && e.Target == c.DependentModel && e.Key == c.ForeignKeyField);
            diagnostics.Add(new SemanticDiagnostic(
                "MDD027",
                $"'{c.PrincipalModel}' ### Relations '{entry.Raw}': 읽기 타입 컬렉션 '{c.Name}' 이 이미 있는 멤버({c.CollidesWith})와 " +
                "이름이 같아 생성하지 않는다 — 이 관계로는 $expand 할 수 없다. 항목의 이름을 바꾸면 컬렉션이 생긴다.",
                entry.Loc ?? principal.Source.Loc,
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

    /// <summary>
    /// A field <c>@system</c> that says nothing, or says the opposite of another declaration. <c>MDD039</c> (warning): on a
    /// field no caller could write anyway — a lookup, a rollup, a computed value, a row version — so the declaration
    /// changes nothing and the author's intent (someone's code writes this) has no column to land on. <c>MDD040</c>:
    /// together with <c>@immutable</c>, which says a caller supplies the value once; the two answer «who supplies it»
    /// differently, and the generator would otherwise pick one without saying so.
    /// </summary>
    private static void CheckSystemDeclaration(ResolvedModel model, FieldNode field, List<SemanticDiagnostic> diagnostics)
    {
        if (!Generation.FieldAuthorship.IsDeclaredSystem(field)) return;

        var noCallerWrites = field.Kind != FieldKind.Stored
            ? $"{field.Kind.ToString().ToLowerInvariant()} 칸은 저장되지 않는다"
            : M3lPrimitives.IsRowVersion(field) ? "행 버전(rowversion)은 엔진이 쓴다" : null;
        if (noCallerWrites is not null)
        {
            diagnostics.Add(new SemanticDiagnostic(
                "MDD039",
                $"'{model.Name}.{field.Name}': @system 은 아무것도 바꾸지 않는다 — {noCallerWrites}. 호출자가 쓸 수 없는 칸이라 쓰기 제외가 " +
                "이미 성립한다. 애플리케이션 코드가 쓰는 값이면 그 값을 담을 저장 칸에 @system 을 두세요.",
                field.Loc,
                SemanticSeverity.Warning));
        }

        if (Ast.FieldAttributes.Has(field, "immutable"))
        {
            diagnostics.Add(new SemanticDiagnostic(
                "MDD040",
                $"'{model.Name}.{field.Name}': @system 과 @immutable 이 함께 있다 — @system 은 «애플리케이션 코드만 쓴다», @immutable 은 " +
                "«호출자가 생성 때 한 번 준다»로 누가 값을 대는지를 다르게 말한다. 하나만 두세요.",
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

        // MDD048 — a shape the language defines and no target generates. An array reached every target as its element
        // type (`string[]` became one string column, one string property) with nothing said; the others were refused
        // as types that do not exist, which misdescribes them.
        if (field.Array)
        {
            diagnostics.Add(UnsupportedShape(model, field, $"배열 `{type}[]`"));
            return;
        }
        if (SpecShapes.Contains(type))
        {
            diagnostics.Add(UnsupportedShape(model, field, type switch
            {
                "enum" => "필드 안에 값을 적는 인라인 `enum`",
                "map" => "`map<K, V>`",
                _ => "하위 필드를 갖는 `object`",
            }));
            return;
        }

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

    /// <summary>Field types the specification defines as shapes rather than names — the parser carries their parts.</summary>
    private static readonly HashSet<string> SpecShapes = new(StringComparer.Ordinal) { "enum", "map", "object" };

    private static SemanticDiagnostic UnsupportedShape(ResolvedModel model, FieldNode field, string shape) => new(
        "MDD048",
        $"'{model.Name}.{field.Name}': {shape} 은 명세가 정의하는 형이지만 이 생성기는 아직 생성하지 않는다 — " +
        "그대로 두면 어느 타깃도 그 모양을 낼 수 없다. 별도 모델과 `@reference` 로 나누거나(`object` · 배열), " +
        "최상위 `::enum` 으로 옮기거나(인라인 enum), 직렬화한 문자열 칸(`text`)으로 둔다.",
        field.Loc);

    /// <summary>
    /// <c>MDD023</c> — <c>@reference(X)?</c> (SET NULL) on a key that cannot hold NULL. The
    /// database refuses the constraint (SQL Server: at creation; PostgreSQL: on the first delete),
    /// so the build stops here instead (M3L §8.4.1: SET NULL "requires nullable field").
    /// </summary>
    private static void CheckSetNullOnRequiredKey(ResolvedModel model, FieldNode field, List<SemanticDiagnostic> diagnostics)
    {
        // MDD046 — an on_delete the rule cannot read, or one that contradicts the symbol.
        if (OnDeleteRule.Problem(model, field) is { } problem)
        {
            diagnostics.Add(new SemanticDiagnostic("MDD046", $"'{model.Name}.{field.Name}': {problem}", field.Loc));
            return;
        }
        if (field.Nullable || OnDeleteRule.For(model, field) != ReferentialAction.SetNull) return;
        diagnostics.Add(new SemanticDiagnostic(
            "MDD023",
            $"'{model.Name}.{field.Name}' 의 SET NULL(@reference(…)? 또는 on_delete: set_null)은 삭제 시 NULL 로 두라는 뜻인데 이 키는 NULL 을 허용하지 않습니다. " +
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
