using System.Text.Json;

namespace MddBooster.Core.Ast;

/// <summary>
/// Rewrites every name a declaration writes qualified — <c>core.Asset</c>, an import alias's <c>inv.Product</c> — to the
/// short name of the declaration the parser resolved it to (the AST's <c>…QualifiedName</c>, §2.1.3).
/// </summary>
/// <remarks>
/// <para>
/// This generator names its outputs by the short name, and <c>MDD053</c> keeps short names unique in a build, so the
/// short name <b>is</b> the declaration. Every name lookup the generator makes then holds for a qualified name as it
/// does for a short one, without each of them learning to read the resolution — one pass, at load, instead of the
/// same reading in every place that matches a name.
/// </para>
/// <para>
/// Rewritten: a parent (<c>inherits</c>), a base, a key's <c>@reference</c> target, a type that names a declaration, a
/// rollup target, a view's <c>from</c> and joins and the relation of its <c>@from(Relation.field)</c>. A name the
/// parser resolved to none — or to more than one — is left as written; the diagnostic for it is the parser's or this
/// generator's. A join's <c>on</c> condition is left as written: it is an expression, and a qualified relation in it is
/// not read here.
/// </para>
/// </remarks>
public static class ShortNames
{
    public static void Apply(M3lAst ast)
    {
        ArgumentNullException.ThrowIfNull(ast);
        foreach (var declaration in ast.Models.Concat(ast.Interfaces).Concat(ast.Views).Concat(ast.Flows))
        {
            for (var i = 0; i < declaration.Inherits.Count && i < declaration.InheritsQualifiedNames.Count; i++)
                if (declaration.InheritsQualifiedNames[i] is { } parent) declaration.Inherits[i] = Short(parent);
            if (declaration.Base is { QualifiedName: { } baseName } b) b.Model = Short(baseName);
            var relations = Sources(declaration);
            Fields(declaration.Fields, relations);
        }
        foreach (var e in ast.Enums)
            for (var i = 0; i < e.Inherits.Count && i < e.InheritsQualifiedNames.Count; i++)
                if (e.InheritsQualifiedNames[i] is { } parent) e.Inherits[i] = Short(parent);
    }

    /// <summary>A view's sources, rewritten — and each one's written name → its short name, for its <c>@from</c> fields.</summary>
    private static Dictionary<string, string> Sources(ModelNode view)
    {
        var relations = new Dictionary<string, string>(StringComparer.Ordinal);
        if (view.SourceDef is not { } source) return relations;
        if (source is { From: { } from, FromQualifiedName: { } fromName })
        {
            relations[from] = Short(fromName);
            source.From = Short(fromName);
        }
        foreach (var join in source.Joins ?? [])
        {
            if (join.QualifiedName is not { } joinName) continue;
            relations[join.Model] = Short(joinName);
            join.Model = Short(joinName);
        }
        return relations;
    }

    private static void Fields(List<FieldNode>? fields, IReadOnlyDictionary<string, string> relations)
    {
        foreach (var field in fields ?? [])
        {
            if (field.TypeQualifiedName is { } type) field.Type = Short(type);
            if (field.ReferenceQualifiedName is { } target) RewriteFirstArg(field, "reference", _ => Short(target));
            if (field.Rollup is { TargetQualifiedName: { } rollupTarget } rollup) rollup.Target = Short(rollupTarget);
            if (relations.Count > 0)
            {
                // `@from(Relation.field)` names its relation the way the view's `from`/`join` wrote it.
                RewriteFirstArg(field, "from", written =>
                {
                    var dot = written.LastIndexOf('.');
                    return dot > 0 && relations.TryGetValue(written[..dot], out var shortName)
                        ? $"{shortName}.{written[(dot + 1)..]}"
                        : written;
                });
            }
            Fields(field.Fields, relations);
        }
    }

    private static void RewriteFirstArg(FieldNode field, string attribute, Func<string, string> rewrite)
    {
        var attr = field.Attributes.FirstOrDefault(a => a.Name == attribute);
        if (attr?.Args is not { Count: > 0 } args || args[0].ValueKind != JsonValueKind.String) return;
        var written = args[0].GetString()!;
        var rewritten = rewrite(written);
        if (rewritten != written) args[0] = JsonSerializer.SerializeToElement(rewritten);
    }

    /// <summary>The short name in a qualified name — the part after the last dot (a namespace has dots, a name has none).</summary>
    private static string Short(string qualifiedName) => qualifiedName[(qualifiedName.LastIndexOf('.') + 1)..];
}
