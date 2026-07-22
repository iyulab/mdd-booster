using MddBooster.Core.Naming;

namespace MddBooster.Tests.Naming;

/// <summary>
/// ADR-0001 §2.1 (U-Platform, 2026-07-22 소비자 확정) 변환 규칙·오류 게이트의 계약 테스트.
/// 규칙: 소문자→대문자 전이 분리 · 대문자 연속(약어)은 한 단어, 뒤에 소문자가 이어지면
/// 마지막 대문자 앞에서 분리 · 숫자는 직전 단어에 붙음. 게이트: `^[a-z][a-z0-9_]*$` ·
/// 63바이트(PG NAMEDATALEN) · fold 충돌 — 전부 오류이며 조용한 보정(절단·치환) 금지.
/// </summary>
public class PostgresIdentifiersTests
{
    // ---- ToSnakeCase: 결정적 변환 ----

    [Theory]
    [InlineData("Asset", "asset")]
    [InlineData("WorkOrder", "work_order")]
    [InlineData("FsaFacilityProfile", "fsa_facility_profile")]
    [InlineData("AssetMaintenanceProfile", "asset_maintenance_profile")]
    [InlineData("FMSCode", "fms_code")]                 // 약어 연속 + 후행 단어
    [InlineData("FMS", "fms")]                          // 전체 약어
    [InlineData("Iso14224Class", "iso14224_class")]     // 숫자는 직전 단어에 붙고, 숫자→대문자에서 분리
    [InlineData("Sha256Hash", "sha256_hash")]
    [InlineData("A", "a")]
    public void ToSnakeCase_ConvertsDeterministically(string pascal, string expected)
    {
        Assert.Equal(expected, PostgresIdentifiers.ToSnakeCase(pascal));
    }

    // ---- Check: 단일 식별자 유효성 게이트 (위반 설명 반환, 유효하면 null) ----

    [Theory]
    [InlineData("asset")]
    [InlineData("asset_id")]
    [InlineData("iso14224_class")]
    public void Check_ValidIdentifier_ReturnsNull(string identifier)
    {
        Assert.Null(PostgresIdentifiers.Check(identifier));
    }

    [Theory]
    [InlineData("Asset")]        // 대문자 — 패턴 위반
    [InlineData("1asset")]       // 숫자 시작
    [InlineData("_asset")]       // 언더스코어 시작
    [InlineData("asset id")]     // 공백
    [InlineData("")]             // 빈 문자열
    public void Check_PatternViolation_ReturnsDescription(string identifier)
    {
        Assert.NotNull(PostgresIdentifiers.Check(identifier));
    }

    [Fact]
    public void Check_Exactly63Bytes_IsValid()
    {
        var name = "a" + new string('b', 62); // 63 bytes
        Assert.Null(PostgresIdentifiers.Check(name));
    }

    [Fact]
    public void Check_Over63Bytes_ReturnsDescription()
    {
        var name = "a" + new string('b', 63); // 64 bytes
        var violation = PostgresIdentifiers.Check(name);
        Assert.NotNull(violation);
        Assert.Contains("63", violation);
    }

    // ---- 예약어 게이트 (게이트 #4) ----
    // ADR 원칙: "인용이 필요한 식별자는 생성하지 않는다" — PG RESERVED_KEYWORD와
    // TYPE_FUNC_NAME_KEYWORD는 테이블/컬럼명으로 비인용 사용이 불가하므로 오류다.
    // (검증: postgresql.org/docs/current/sql-keywords-appendix.html, 2026-07-23)

    [Theory]
    [InlineData("order")]   // RESERVED — CREATE TABLE public.order 는 구문 오류
    [InlineData("user")]
    [InlineData("group")]
    [InlineData("select")]
    [InlineData("check")]
    [InlineData("binary")]  // TYPE_FUNC_NAME — 테이블명 불가
    [InlineData("ilike")]
    public void Check_ReservedKeyword_ReturnsDescription(string identifier)
    {
        var violation = PostgresIdentifiers.Check(identifier);
        Assert.NotNull(violation);
        Assert.Contains("예약어", violation);
    }

    [Theory]
    [InlineData("between")] // non-reserved(COL_NAME) — 테이블/컬럼명 허용
    [InlineData("bigint")]
    [InlineData("boolean")]
    [InlineData("status")]
    [InlineData("user_id")] // 예약어를 부분 문자열로 포함하는 것은 무관
    public void Check_NonReservedKeyword_IsValid(string identifier)
    {
        Assert.Null(PostgresIdentifiers.Check(identifier));
    }

    [Fact]
    public void BuildTableNameMap_ReservedFold_Throws()
    {
        // 모델 Order → order (PG 예약어) — 조용한 인용 처리 금지, 오류
        var ex = Assert.Throws<PostgresNamingException>(
            () => PostgresIdentifiers.BuildTableNameMap(["Order"]));

        Assert.Contains("Order", ex.Message);
        Assert.Contains("예약어", ex.Message);
    }

    // ---- BuildTableNameMap: 전 모델 일괄 변환 + 게이트 (위반은 전부 모아 한 번에 오류) ----

    [Fact]
    public void BuildTableNameMap_MapsAllModels()
    {
        var map = PostgresIdentifiers.BuildTableNameMap(["Asset", "WorkOrder", "FsaFacilityProfile"]);

        Assert.Equal("asset", map["Asset"]);
        Assert.Equal("work_order", map["WorkOrder"]);
        Assert.Equal("fsa_facility_profile", map["FsaFacilityProfile"]);
    }

    [Fact]
    public void BuildTableNameMap_FoldCollision_ThrowsWithBothModelNames()
    {
        // FMSCode와 FmsCode는 모두 fms_code로 fold — 무경고 진행 금지
        var ex = Assert.Throws<PostgresNamingException>(
            () => PostgresIdentifiers.BuildTableNameMap(["FMSCode", "FmsCode"]));

        Assert.Contains("FMSCode", ex.Message);
        Assert.Contains("FmsCode", ex.Message);
        Assert.Contains("fms_code", ex.Message);
    }

    [Fact]
    public void BuildTableNameMap_Over63Bytes_ThrowsWithoutTruncation()
    {
        var longModel = string.Concat(Enumerable.Repeat("Verylongsegment", 5)); // snake 결과 > 63바이트
        var ex = Assert.Throws<PostgresNamingException>(
            () => PostgresIdentifiers.BuildTableNameMap([longModel]));

        Assert.Contains(longModel, ex.Message);
    }

    [Fact]
    public void BuildTableNameMap_MultipleViolations_AllReportedAtOnce()
    {
        var longModel = string.Concat(Enumerable.Repeat("Verylongsegment", 5));
        var ex = Assert.Throws<PostgresNamingException>(
            () => PostgresIdentifiers.BuildTableNameMap(["FMSCode", "FmsCode", longModel]));

        // 충돌 1건 + 길이 1건이 한 예외에 모두 담겨야 한다 (하나씩 고쳐가며 재실행 금지)
        Assert.Contains("fms_code", ex.Message);
        Assert.Contains(longModel, ex.Message);
        Assert.Equal(2, ex.Violations.Count);
    }
}
