using MddBooster.Core.Naming;

namespace MddBooster.Tests.Naming;

/// <summary>
/// 단어분리 정본(ADR-0001 §2.1)과 camelCase 변환의 계약 테스트.
/// <para>
/// 이 클래스가 존재하는 이유: 같은 분리 규칙이 snake_case 경로에는 있었지만 camelCase 경로에는
/// 없어서 <c>QRScanLog</c>가 <c>qr_scan_log</c>(정상)와 <c>qRScanLog</c>(훼손)로 갈라졌다.
/// 분리기를 <see cref="NameCasing"/>으로 올린 뒤, 두 경로가 같은 규칙을 쓴다는 것을 여기서 고정한다.
/// </para>
/// </summary>
public class NameCasingTests
{
    // ---- SplitWords: 원본 대소문자를 보존해야 한다 ----

    [Theory]
    [InlineData("QRScanLog", "QR|Scan|Log")]
    [InlineData("QRCode", "QR|Code")]
    [InlineData("OrderQR", "Order|QR")]      // 후행 약어 — 한 조각으로 유지
    [InlineData("Order", "Order")]
    [InlineData("SMS", "SMS")]               // 전체 약어 — 분리 없음
    [InlineData("Iso14224Class", "Iso14224|Class")]  // 숫자는 직전 단어에, 숫자→대문자에서 분리
    [InlineData("A", "A")]
    public void SplitWords_PreservesOriginalCasing(string pascal, string expectedJoined)
    {
        // 소문자화된 조각을 돌려주면 camelCase 가 후행 약어를 되살릴 수 없다 (OrderQR → orderQr).
        // 그래서 분리기의 계약은 "경계만 찾고 대소문자는 건드리지 않는다" 이다.
        Assert.Equal(expectedJoined, string.Join('|', NameCasing.SplitWords(pascal)));
    }

    [Fact]
    public void SplitWords_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(NameCasing.SplitWords(""));
    }

    // ---- ToCamelCase: 첫 단어만 소문자화 (.NET JsonNamingPolicy.CamelCase 와 동일) ----

    [Theory]
    [InlineData("QRScanLog", "qrScanLog")]
    [InlineData("QRCode", "qrCode")]
    [InlineData("OrderQR", "orderQR")]       // 후행 약어 보존
    [InlineData("Order", "order")]
    [InlineData("OrderItem", "orderItem")]
    [InlineData("SMS", "sms")]
    [InlineData("QR", "qr")]
    [InlineData("QRs", "qRs")]               // 두 글자 약어 + 소문자 s — 표준 규칙의 결과. 아래 주석 참조
    [InlineData("A", "a")]
    public void ToCamelCase_LowercasesOnlyTheFirstWord(string pascal, string expected)
    {
        // "QRs" → "qRs" 가 어색해 보이지만 표준 규칙의 정확한 결과다('R' 뒤에 소문자가 오므로
        // 약어 연쇄가 끊긴다). 그래서 ApiRegistrationRenderer 는 **camelCase 를 먼저** 적용하고
        // 복수화한다 — "QR" → "qr" → "qrs". 순서가 이 어색함을 애초에 만들지 않게 한다.
        Assert.Equal(expected, NameCasing.ToCamelCase(pascal));
    }

    [Fact]
    public void ToCamelCase_EmptyInput_ReturnsInput()
    {
        Assert.Equal("", NameCasing.ToCamelCase(""));
    }

    // ---- ToPascalCase: snake_case 를 올린다 (입력 알파벳이 반대 방향) ----

    [Theory]
    [InlineData("work_order", "WorkOrder")]
    [InlineData("id", "Id")]
    [InlineData("byte_size", "ByteSize")]
    [InlineData("in_review", "InReview")]
    [InlineData("qr_code", "QrCode")]          // 이미 소문자인 약어는 되살리지 않는다
    [InlineData("QR_code", "QRCode")]          // 첫 글자 외에는 원본 유지
    [InlineData("a", "A")]
    [InlineData("__leading", "Leading")]       // 빈 조각은 버린다
    [InlineData("double__underscore", "DoubleUnderscore")]
    [InlineData("trailing_", "Trailing")]
    public void ToPascalCase_RaisesEachUnderscoreSeparatedWord(string snake, string expected)
    {
        Assert.Equal(expected, NameCasing.ToPascalCase(snake));
    }

    [Fact]
    public void ToPascalCase_EmptyInput_ReturnsInput()
    {
        Assert.Equal("", NameCasing.ToPascalCase(""));
    }

    [Fact]
    public void Enum_type_and_member_are_raised_by_the_same_conversion()
    {
        // An enum member name is raised twice by different renderers — once for the
        // member declaration and once for a property initializer that references it.
        // If those two conversions were separate implementations that drifted, the
        // initializer would name a member that does not exist and the generated code
        // would not compile. One implementation is what makes that impossible; this
        // pins the property rather than trusting that nobody re-adds a private copy.
        const string enumType = "asset_status";
        const string member = "in_review";

        var declaration = $"{NameCasing.ToPascalCase(enumType)}.{NameCasing.ToPascalCase(member)}";

        Assert.Equal("AssetStatus.InReview", declaration);
    }

    // ---- 두 경로가 같은 분리기를 쓴다는 것 자체를 고정 ----

    [Theory]
    [InlineData("QRScanLog", "qr_scan_log", "qrScanLog")]
    [InlineData("FMSCode", "fms_code", "fmsCode")]
    [InlineData("WorkOrder", "work_order", "workOrder")]
    public void SnakeAndCamel_ShareTheSameWordBoundaries(string pascal, string snake, string camel)
    {
        // 이 테스트가 깨지면 두 경로 중 하나가 정본 분리기에서 떨어져 나간 것이다 —
        // 이 저장소가 애초에 겪었던 결함이 재발했다는 뜻.
        Assert.Equal(snake, PostgresIdentifiers.ToSnakeCase(pascal));
        Assert.Equal(camel, NameCasing.ToCamelCase(pascal));
    }
}
