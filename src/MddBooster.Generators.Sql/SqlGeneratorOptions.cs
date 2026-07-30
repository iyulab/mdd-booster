namespace MddBooster.Generators.Sql;

public sealed class SqlGeneratorOptions
{
    /// <summary>
    /// .sqlproj 파일이 있는 프로젝트 루트 경로.
    /// 상대 경로이면 GeneratorContext.WorkingDirectory 기준으로 해석.
    /// </summary>
    public required string ProjectPath { get; init; }

    /// <summary>
    /// T-SQL 스키마 이름 (예: "dbo")
    /// </summary>
    public string Schema { get; init; } = "dbo";

    /// <summary>
    /// <c>.sqlproj</c> ItemGroup 패치(생성 파일 등록) 방출 여부.
    /// SSDT 대신 desired-state 기반 도구(Schemorph 등)로 스키마를 소비할 때 <c>false</c>로 두면
    /// <c>.sqlproj</c>를 은퇴시킬 수 있다. <c>false</c>이면 <c>.sqlproj</c> 탐색도 건너뛰므로
    /// 프로젝트에 <c>.sqlproj</c>가 없어도 build가 성공한다. 기본값 <c>true</c>(현행 동작).
    /// </summary>
    public bool EmitSqlProj { get; init; } = true;

    /// <summary>
    /// post-deployment 뷰 갱신 스크립트(<c>Scripts_gen/Script.PostDeployment.RefreshViews.sql</c>,
    /// 뷰별 <c>sp_refreshview</c>) 방출 여부. 기본값 <c>true</c>(현행 동작).
    /// <see cref="EmitSqlProj"/>와 독립적으로 제어된다 — 두 아티팩트의 차단 상태가 다르므로
    /// 하나의 노브로 합치지 않는다.
    /// </summary>
    public bool EmitRefreshScript { get; init; } = true;

    /// <summary>
    /// enum 컬럼에 table-level <c>CK_{Table}_{Column}</c> CHECK 제약 방출 여부.
    /// 기본값 <c>false</c> — SSDT dacpac은 CHECK를 매번 Drop→Create로 재현해 diff가
    /// 불안정하므로(정책: cycle 27) SSDT 소비자는 EF Core 변환 검증에 의존한다.
    /// 선언형 도구(Schemorph 등) 소비자는 <c>true</c>로 DB 레벨 enum 강제를 켤 수 있다.
    /// </summary>
    public bool EmitEnumCheckConstraints { get; init; }

    /// <summary>
    /// 외래 키 컬럼에 인덱스를 자동 생성할지. 기본값 <c>false</c>.
    /// 엔진은 FK를 자동 인덱싱하지 않으므로 조인·삭제 검사가 자식 테이블을 훑는다.
    /// 켜면 <b>선언되지 않은 FK마다 인덱스 하나</b>가 늘어난다 — 읽기 이득과 쓰기·저장 비용의
    /// 교환이며, 기존 스키마에 조용히 적용할 판단이 아니므로 opt-in이다.
    /// 모델이 이미 덮은 컬럼(<c>@pk</c>·<c>@unique</c>·<c>@index</c>, 또는 복합 인덱스의 선두
    /// 컬럼)은 <see cref="ForeignKeyIndexPlanner"/>가 걸러낸다.
    /// </summary>
    public bool EmitForeignKeyIndexes { get; init; }
}
