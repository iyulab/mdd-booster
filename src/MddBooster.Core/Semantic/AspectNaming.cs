using MddBooster.Core.Naming;

namespace MddBooster.Core.Semantic;

/// <summary>
/// <c>::aspect</c> 이름 규칙의 정본. base 쪽에 생기는 navigation 의 이름이 여기서 하나로
/// 정해진다.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ModelPrimaryKey"/> 와 같은 자리에 있는 이유도 같다 — 이 답을 필요로 하는 곳이
/// 하나가 아니다. 진단(이름 약속 경고·충돌 오류) · EF 구성 · 테이블 주석 · API 표면이 전부
/// 같은 이름을 말해야 하고, 각자 계산하면 어긋나는 순간 그것을 말해 주는 것이 없다.
/// </para>
/// <para>
/// 규칙: 모델명이 <c>&lt;Base&gt;</c> 또는 <c>&lt;Prefix&gt;&lt;Base&gt;</c> 로 시작하면 그
/// 뒤의 나머지가 navigation 이름이고 prefix 는 보존된다. 그렇지 않으면 <b>모델명 전체</b>다 —
/// 줄일 근거가 없는 이름을 임의로 줄이면 서로 다른 모델이 같은 이름으로 무너진다.
/// </para>
/// </remarks>
public static class AspectNaming
{
    /// <summary>
    /// base 쪽 navigation 이름과, 그것이 이름 약속에서 나온 것인지.
    /// </summary>
    public static (string Name, bool MatchesConvention) ReverseNavigation(ResolvedModel model, string baseName)
    {
        var prefix = PascalPrefix(model);

        if (prefix.Length > 0
            && model.Name.StartsWith(prefix + baseName, StringComparison.Ordinal)
            && model.Name.Length > prefix.Length + baseName.Length)
        {
            return (prefix + model.Name[(prefix.Length + baseName.Length)..], true);
        }

        if (model.Name.StartsWith(baseName, StringComparison.Ordinal)
            && model.Name.Length > baseName.Length)
        {
            return (model.Name[baseName.Length..], true);
        }

        return (model.Name, false);
    }

    /// <summary>파일이 선언한 <c># Prefix:</c> 를 PascalCase 로. 없으면 빈 문자열.</summary>
    public static string PascalPrefix(ResolvedModel model)
        => model.Source.Prefix is { Length: > 0 } p ? NameCasing.ToPascalCase(p) : "";
}
