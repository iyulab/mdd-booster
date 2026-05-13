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
/// Current scope (Cycle 24): reference integrity + enum resolution. Deeper
/// checks (lookup dotted-path navigation, rollup aggregate compatibility,
/// circular inheritance) are reserved for a later cycle so this stays a
/// focused, fast pass.
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
                CheckLookupPath(model, field, diagnostics);
                CheckRollupTarget(model, field, diagnostics);
                CheckBinding(model, field, diagnostics);
            }
        }

        return diagnostics;
    }

    private void CheckLookupPath(ResolvedModel model, FieldNode field, List<SemanticDiagnostic> diagnostics)
    {
        if (field.Kind != FieldKind.Lookup || field.Lookup is null) return;
        var path = field.Lookup.Path;
        if (string.IsNullOrWhiteSpace(path)) return;

        var dot = path.IndexOf('.');
        if (dot <= 0 || dot >= path.Length - 1)
        {
            diagnostics.Add(new SemanticDiagnostic(
                "MDD003",
                $"'{model.Name}.{field.Name}' @lookup 경로 '{path}'가 'fk.column' 형태가 아닙니다.",
                field.Loc));
            return;
        }

        var fkFieldName = path[..dot];
        var targetColumn = path[(dot + 1)..];

        var fkField = model.Fields.FirstOrDefault(f => f.Name == fkFieldName);
        if (fkField is null)
        {
            diagnostics.Add(new SemanticDiagnostic(
                "MDD004",
                $"'{model.Name}.{field.Name}' @lookup({path}): 동일 모델에 FK 필드 '{fkFieldName}'가 존재하지 않습니다.",
                field.Loc));
            return;
        }

        var refAttr = fkField.Attributes.FirstOrDefault(a =>
            string.Equals(a.Name, "reference", StringComparison.OrdinalIgnoreCase));
        if (refAttr?.Args is null || refAttr.Args.Count == 0)
        {
            diagnostics.Add(new SemanticDiagnostic(
                "MDD005",
                $"'{model.Name}.{field.Name}' @lookup({path}): FK 필드 '{fkFieldName}'에 @reference(Target) 속성이 없습니다.",
                field.Loc));
            return;
        }

        var targetName = refAttr.Args[0].ValueKind == System.Text.Json.JsonValueKind.String
            ? refAttr.Args[0].GetString()
            : refAttr.Args[0].GetRawText();

        if (string.IsNullOrEmpty(targetName) || !_modelNames.Contains(targetName)) return;

        var targetModel = _models.First(m => m.Name == targetName);
        if (!targetModel.Fields.Any(f => f.Name == targetColumn))
        {
            diagnostics.Add(new SemanticDiagnostic(
                "MDD006",
                $"'{model.Name}.{field.Name}' @lookup({path}): 대상 엔티티 '{targetName}'에 필드 '{targetColumn}'가 존재하지 않습니다.",
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

    private void CheckReferenceTarget(ResolvedModel model, FieldNode field, List<SemanticDiagnostic> diagnostics)
    {
        var refAttr = field.Attributes.FirstOrDefault(a =>
            string.Equals(a.Name, "reference", StringComparison.OrdinalIgnoreCase));
        if (refAttr?.Args is null || refAttr.Args.Count == 0) return;

        var target = refAttr.Args[0].ValueKind == System.Text.Json.JsonValueKind.String
            ? refAttr.Args[0].GetString()
            : refAttr.Args[0].GetRawText();

        if (string.IsNullOrEmpty(target)) return;
        if (_modelNames.Contains(target)) return;

        diagnostics.Add(new SemanticDiagnostic(
            "MDD002",
            $"'{model.Name}.{field.Name}' 필드의 @reference({target}) 대상 엔티티가 존재하지 않습니다.",
            field.Loc));
    }
}

public sealed record SemanticDiagnostic(string Code, string Message, SourceLocation? Location)
{
    public string Format()
    {
        var loc = Location is null
            ? string.Empty
            : $" {Location.File}:{Location.Line}:{Location.Col}";
        return $"[{Code}]{loc} {Message}";
    }
}
