using MddBooster.Core.Naming;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Sql;

/// <summary>
/// A referenced model's primary-key column as T-SQL names it. Every place that joins or
/// constrains against another model's key — the FK clause, a lookup JOIN, a rollup's
/// correlation — reads it from here rather than assuming <c>[Id]</c>: a model's key is
/// whatever field carries <c>@pk</c> (a shared-key extension's <c>asset_id</c>, a
/// <c>code @pk</c>), and a hard-coded <c>[Id]</c> points at a column that table lacks.
/// </summary>
internal static class TargetKey
{
    /// <summary>The model's PK column in PascalCase, e.g. <c>Id</c>, <c>AssetId</c>, <c>Code</c>.</summary>
    public static string PkColumn(ResolvedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var pk = ModelPrimaryKey.Find(model)
            ?? throw new InvalidOperationException(
                $"모델 '{model.Name}'에 PK(@pk)가 없어 그 키를 가리키는 SQL 을 렌더할 수 없습니다.");
        return NameCasing.ToPascalCase(pk.Name);
    }

    /// <summary>Resolves <paramref name="target"/> or throws with the referring site named.</summary>
    public static ResolvedModel Resolve(string target, Func<string, ResolvedModel?>? findModel, string referrer)
    {
        var model = findModel?.Invoke(target);
        return model ?? throw new InvalidOperationException(
            $"{referrer}의 참조 대상 모델 '{target}'을(를) 찾을 수 없습니다 — 대상 모델 목록을 함께 넘겨야 합니다.");
    }
}
