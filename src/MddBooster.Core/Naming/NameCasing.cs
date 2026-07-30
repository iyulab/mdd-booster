namespace MddBooster.Core.Naming;

/// <summary>
/// 모델명(PascalCase)의 **단어분리 정본**과 그 위에 얹히는 대소문자 변환.
/// 분리 규칙은 ADR-0001 §2.1 — 소문자/숫자→대문자 전이에서 분리, 대문자 연속(약어)은 한 단어로
/// 두되 뒤에 소문자가 이어지면 마지막 대문자 앞에서 분리, 숫자는 직전 단어에 붙는다.
/// </summary>
/// <remarks>
/// 이 규칙은 원래 <see cref="PostgresIdentifiers.ToSnakeCase"/>에만 있었고, camelCase가 필요한
/// 두 표면(Api의 GraphQL 필드명, TypeScript 폼 헬퍼 이름)은 각자 <c>char.ToLowerInvariant(s[0]) + s[1..]</c>
/// 라는 손구현 사본을 갖고 있었다. 그래서 <c>QRScanLog</c>가 PG 경로에서는 <c>qr_scan_log</c>로
/// 올바르게 나오는데 camelCase 경로에서는 <c>qRScanLog</c>로 훼손됐다. 규칙이 한 곳에만 살도록
/// 분리기를 여기로 올리고 두 사본을 제거했다.
/// <para>
/// <see cref="SplitWords"/>는 **원본 대소문자를 보존한 조각**을 돌려준다. 소문자화는 호출부의
/// 책임이다 — 분리기가 미리 소문자화하면 camelCase가 후행 약어를 되살릴 수 없어
/// <c>OrderQR</c>이 <c>orderQr</c>로 깨진다.
/// </para>
/// </remarks>
public static class NameCasing
{
    /// <summary>
    /// PascalCase 이름을 단어 조각으로 분리한다. **대소문자는 원본 그대로 보존**한다
    /// (예: <c>QRScanLog</c> → <c>["QR", "Scan", "Log"]</c>, <c>OrderQR</c> → <c>["Order", "QR"]</c>).
    /// </summary>
    public static IReadOnlyList<string> SplitWords(string pascalName)
    {
        if (string.IsNullOrEmpty(pascalName)) return [];

        var words = new List<string>();
        var start = 0;
        for (var i = 1; i < pascalName.Length; i++)
        {
            if (StartsNewWord(pascalName, i))
            {
                words.Add(pascalName[start..i]);
                start = i;
            }
        }
        words.Add(pascalName[start..]);
        return words;
    }

    /// <summary>
    /// 위치 <paramref name="i"/>(항상 &gt; 0)에서 새 단어가 시작하는지. 분리 조건은
    /// <see cref="PostgresIdentifiers.ToSnakeCase"/>가 원래 갖고 있던 판정과 문자 그대로 동일하다.
    /// </summary>
    private static bool StartsNewWord(string s, int i)
    {
        if (!char.IsUpper(s[i])) return false;

        var prev = s[i - 1];
        if (char.IsLower(prev) || char.IsDigit(prev)) return true;

        // 약어 연쇄의 끝 — 다음 글자가 소문자면 이 대문자부터 새 단어다 (QRScanLog → QR|ScanLog).
        return char.IsUpper(prev) && i + 1 < s.Length && char.IsLower(s[i + 1]);
    }

    /// <summary>
    /// PascalCase → camelCase. **첫 단어만** 소문자화하고 나머지는 원본 그대로 둔다 —
    /// .NET <c>JsonNamingPolicy.CamelCase</c>와 같은 결과다.
    /// <c>QRScanLog</c>→<c>qrScanLog</c> · <c>QRCode</c>→<c>qrCode</c> ·
    /// <c>OrderQR</c>→<c>orderQR</c>(후행 약어 보존) · <c>Order</c>→<c>order</c>.
    /// </summary>
    public static string ToCamelCase(string pascalName)
    {
        if (string.IsNullOrEmpty(pascalName)) return pascalName;

        var words = SplitWords(pascalName);
        return string.Concat(
            words.Select((w, idx) => idx == 0 ? w.ToLowerInvariant() : w));
    }
}
