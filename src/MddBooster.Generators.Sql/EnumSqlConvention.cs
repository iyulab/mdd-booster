using M3L.Native;

namespace MddBooster.Generators.Sql;

/// <summary>
/// enum 값의 SQL 표현 정본. 컬럼 폭 산정(<see cref="ColumnLength"/>)과 CHECK 제약
/// 리터럴(<see cref="CheckValues"/>)이 같은 값 직렬화 규약(snake_case 원문, N'..' 이스케이프)을
/// 공유하도록 한 곳에 둔다. EF Core 측은 <c>[EnumMember]</c> snake_case 값을 동일하게
/// 저장하므로 세 소비자(컬럼 폭·CHECK·EF 변환)가 같은 문자열 집합 위에 선다.
/// </summary>
public static class EnumSqlConvention
{
    /// <summary>CHECK ([Col] IN (...)) 안에 들어가는 리터럴 목록.</summary>
    public static string CheckValues(EnumNode enumNode)
    {
        ArgumentNullException.ThrowIfNull(enumNode);
        return string.Join(", ",
            enumNode.Values.Select(v => "N'" + (v.Name ?? string.Empty).Replace("'", "''") + "'"));
    }

    /// <summary>
    /// enum 컬럼 NVARCHAR 폭 — 최장 멤버 길이, 하한 20 (늦은 멤버 추가로 인한
    /// 컬럼 리사이즈를 줄이는 여유).
    /// </summary>
    public static int ColumnLength(EnumNode enumNode)
    {
        ArgumentNullException.ThrowIfNull(enumNode);
        return Math.Max(20, enumNode.Values.Count == 0
            ? 20
            : enumNode.Values.Max(v => (v.Name ?? string.Empty).Length));
    }
}
