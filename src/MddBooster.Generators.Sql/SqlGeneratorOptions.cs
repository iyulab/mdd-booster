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
}
