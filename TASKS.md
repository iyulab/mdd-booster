# mdd-booster 남은 작업 정리

본 문서는 [설계 스펙](claudedocs/plans/2026-04-05-mdd-booster-rewrite-design.md)의 나머지 구현 범위를 플랜 단위로 정리한다.

- **Last updated**: 2026-04-06 (세 번째 20-cycle run 마감, Yesung Plan 5 통합 완료)
- **Completed**:
  - Plan 1 — SQL Generator MVP
  - Plan 2 — iyu-framework-v5 런타임 뼈대 (57 tests)
  - **Plan 3 — C# 엔티티/Enum/DbContext 생성기** (C21-C24, C31-C34)
  - **Plan 4 — API 생성기** (C25-C27, 세 번째 run C3/C4에서 ODataControllerRenderer 추가 완성)
  - **Plan 5 — Yesung 전체 통합 완료** (세 번째 run C1-C9)
    - yesung/mdd/mdd.json 3-타깃 (Sql + Model + Api) 전환
    - yesung/src/Yesung 레거시 (Entity_/Gql_/Models_) 제거, 생성물 기반 재구성
    - Yesung.MainServer ApiRegistration.RegisterGeneratedEntities 자동 호출
    - HTTP smoke: $metadata 200, POST 201 (BankAccount, Enterprise), GraphQL schema 14 query fields
    - **135 tests (+9 in this run)**: 126 → 128 (API using) → 130 (OData controller) → 131 (string literal) → 132 (sibilant+format) → 134 (CTE) → 135 (nullable lookup)
- **Plan 5 세 번째 run에서 수정한 latent buts**:
  1. **C3** — `ApiRegistrationRenderer`가 entity namespace using 미방출 → Api/Model target namespace 다를 때 컴파일 실패
  2. **C4** — Plan 4에서 "엔티티별 컨트롤러 생성 안 함" 선언이 **미구현**이었음. 제네릭/abstract `IyuODataController<TRead,TWrite>`는 MVC ControllerFeatureProvider가 필터로 걸러냄 → 모든 OData 엔드포인트 404. 신규 `ODataControllerRenderer`가 per-entity thin subclass를 `Controllers_gen.cs`에 생성하여 해결.
  3. **C5** — `ExtViewRenderer.NormalizeComputedExpression`이 단일 따옴표 문자열 리터럴 안의 snake_case도 PascalCase로 변환 → `'taxable'` → `'[Taxable]'`. 토크나이저 풍으로 재작성, literal copy-verbatim.
  4. **C6** — `Pluralizer` sibilant 규칙 누락(`Address` → `Address`), `SqlProjPatcher` 결과 한 줄 연결.
  5. **C7** — ExtViewRenderer가 동일 SELECT alias sibling reference를 전제. SQL Server 금지. **CTE 계단식 레이어링**으로 구조 재작성.
  6. **C8** — Lookup 필드 C# 속성이 FK nullability 미반영. nullable FK를 경유한 lookup은 `string?`으로 생성되어야 함.
- **Next** — 세 번째 run 종료. 모든 Plan 1-5 완료. 차후 작업은 `## Plan 5 잔여 항목` 및 `## 횡단 관심사` 섹션의 보류 플래그 참조.

---

## Plan 2 — iyu-framework-v5 런타임 뼈대 ✅ COMPLETED (2026-04-05)

**저장소**: `D:\data\iyu-framework-v5` (5 프로젝트 + 1 테스트 프로젝트, 57 tests)
**결과물**: `claudedocs/cycle-logs/cycle-01.md` .. `cycle-14.md` 참조. E2E smoke via Yesung.MainServer (HTTP OData + GraphQL) 검증 완료.

### 2.1 Iyu.Core 프로젝트 (net10.0 classlib)
- [x] `IyuEntity` 추상 베이스 클래스 (Id, CreatedAt, UpdatedAt 공통 필드 컨벤션)
- [x] Value Object 레코드: `PhoneNumber`, `EmailAddress`, `WebUrl` (+ 검증 로직)
- [x] Attribute 마커 패밀리 (`[Lookup]`, `[Rollup]`, `[Computed]`, `[Reference]`)
- [x] 네임스페이스 구조 확정 (`Iyu.Core.Entities`, `Iyu.Core.ValueObjects`, `Iyu.Core.Attributes`)

### 2.2 Iyu.Data 프로젝트
- [x] `IyuDbContext` 베이스 + `IyuTimestampInterceptor` (CreatedAt/UpdatedAt 자동 채움, CreatedAt immutable guard)
- [x] `IyuValueConverters` — PhoneNumber/EmailAddress/WebUrl ↔ string 양방향
- [x] Read/Write 페어는 `IyuEntityPairRegistry` (Iyu.Server.OData)로 런타임 레지스트리 관리 (마커 인터페이스 대신)
- [x] EF Core 10.0.5 PackageReference

### 2.3 Iyu.Server.OData 프로젝트
- [x] `IyuEdmModelBuilder.AddEntityPair<TRead, TWrite>(setName)` + `IyuEntityPairRegistry`
- [x] `IyuODataController<TRead, TWrite>` 제네릭 — GET/POST/PATCH/DELETE (PATCH delta-aware, Id/CreatedAt/UpdatedAt 스킵)
- [x] 관례 라우팅은 소비 앱의 thin 서브클래스 (2줄)로 해결 — 단일 dispatcher 컨벤션은 Plan 4에서 선택적 추가
- [x] Microsoft.AspNetCore.OData 9.4.1

### 2.4 Iyu.Server.GraphQL 프로젝트
- [x] `IyuGraphQLSchemaBuilder` — HotChocolate 14.3.0
- [x] `AddEntityPair<TRead, TWrite>(queryName, mutationPrefix)` — Query 필드 자동 등록 (Mutation은 Plan 4에서 제너레이터 책임)
- [x] `MapGraphQL()` 기본 `/graphql` 엔드포인트
- [x] HotChocolate.AspNetCore + HotChocolate.Data.EntityFramework

### 2.5 Iyu.MainServer 컴포지트
- [x] `AddIyuMainServer<TContext>()` — EF + OData + GraphQL 한 줄 부트스트랩
- [x] `UseIyuMainServer()` — 라우팅/엔드포인트 설정
- [x] 임시 구현 전면 교체

### 2.6 솔루션/테스트
- [x] `IyuFramework.slnx` — 5개 프로젝트 + Iyu.Tests
- [x] `Iyu.Tests` — 57 tests (Attributes 3, Entities 2, VOs 34, Data 6, OData 10, GraphQL 3 — 대략 분포)
- [x] Yesung.MainServer E2E smoke — `POST /$data/BankAccounts` 201 + `POST /graphql { bankAccounts }` 200 검증 (C13)

---

## Plan 3 — C# 엔티티/Enum/DbContext 생성기 ✅ COMPLETED

**저장소**: `D:\data\mdd-booster` (`MddBooster.Generators.Model`)
**완료**: 두 번째 20-cycle run (C15-C20, C21-C24, C31-C34)

### 완료 항목
- [x] 3.1 MddBooster.Generators.Model 프로젝트 스캐폴드
- [x] 3.2 Read/Write 엔티티 쌍 생성 (IOrder/Order/OrderExt, 상속 없음, ExtBacking 3-way)
- [x] 3.3 Enum 생성 (C# enum + `[EnumMember(Value="snake_case")]` + SQL `CHECK (Col IN ...)` 제약 + `nvarchar(n)` 자동 길이)
- [x] 3.4 Value Object 매핑 (PhoneNumber/EmailAddress/WebUrl)
- [x] 3.5 DbContext 생성 (`DbSet<T>`/`DbSet<TExt>` + 파생 필드 모델의 자동 `ToView` 매핑)
- [x] 3.6 SDK-style csproj glob 포함
- [x] 3.7 ModelGenerator 테스트 (E2E Roslyn 파싱 + Yesung 전체 14엔티티 acceptance)
- [x] 3.8 SQL 생성기 FK + Enum CHECK
- [x] 추가: SemanticAnalyzer (MDD001-MDD009 — reference/lookup path/rollup target 무결성)
- [x] 추가: Lookup/Rollup/Computed Read 필드 렌더링 ([Iyu.Core.Attributes.Lookup/Rollup/Computed])

### 보류 (필요성 낮음 또는 미래 작업)
- [ ] `### Indexes` 섹션 → 일반 인덱스 (Yesung 픽스처에서 미사용)
- [ ] 복합 `@unique(col1, col2)` — Yesung 픽스처에서 미사용

---

## Plan 4 — OData + GraphQL API 생성기 ✅ COMPLETED

**저장소**: `D:\data\mdd-booster` (`MddBooster.Generators.Api`)
**완료**: 두 번째 20-cycle run (C25-C27)

### 완료 항목
- [x] 4.1 Generators.Api 프로젝트 스캐폴드 + `ApiRegistrationGenerator`
- [x] 4.2 `Api_gen/ApiRegistration_gen.cs` 단일 파일 생성
  - `options.ODataModel.AddEntityPair<OrderExt, Order>("Orders")`
  - `options.GraphQL.AddEntityPair<OrderExt, Order>("orders", "order")`
  - 복수형 변환 (Core.Naming.Pluralizer)
- [x] 4.3 엔티티별 컨트롤러 생성 안 함 — 제네릭 `IyuODataController<TRead, TWrite>`
- [x] 4.4 골든 파일 + Roslyn 구문 검증 + 풀 픽스처 E2E

### 보류 (Plan 5의 Yesung 통합에 포함)
- [ ] 실제 Yesung.MainServer에 생성된 ApiRegistration 적용 후 HTTP smoke

---

## Plan 5 — Yesung 전체 통합 + Lookup/Rollup/Computed 뷰

**저장소**: `D:\data\yesung` + `D:\data\mdd-booster`
**의존**: Plan 2/3/4 완료
**생성기 부분 완료**: 두 번째 20-cycle run (C28-C33)

### 5.1 뷰 생성기 (완료)
- [x] `ViewPlanner` — 판정 (C28)
- [x] `FullViewRenderer` — LEFT JOIN (C29)
- [x] `ExtViewRenderer` — 서브쿼리 집계 + computed (C30)
- [x] `@rollup @indexed` → `WITH SCHEMABINDING`
- [ ] 단일행 `@computed` → `PERSISTED COMPUTED COLUMN` (현재는 뷰로만 투영 — 차후 최적화)
- [x] `Views_gen/` 폴더 + SqlProjPatcher 다중 폴더 (C31, C35)

### 5.2 ModelGenerator 뷰 엔티티 (완료)
- [x] `{Entity}Ext` — Lookup/Rollup/Computed 필드 렌더링 (C22)
- [x] `DbSet<{Entity}Ext>` + 자동 `ToView` 매핑 (C31, C34)

### 5.3 Yesung tables.m3l.md 전체 적용 (생성기 검증 완료, 실제 도메인 적용 대기)
- [x] 생성기 acceptance 통과 — 14 엔티티 + 13 enum (C32 YesungFullFixtureTests)
- [x] SemanticAnalyzer 통과 (C33)
- [ ] `yesung/mdd/mdd.json`의 `sources`를 `./tables.m3l.md`로 복원 (사용자 승인 필요)
- [ ] 실제 Yesung 프로젝트 구조에 생성 파일 배치 (레거시와 충돌 정리 선행)
- [ ] SSDT 빌드 검증 (VS 또는 MSBuild 환경 필요)

### 5.4 레거시 정리 ✅ (세 번째 run C2-C3)
- [x] `yesung/src/Yesung/Entity_`, `Gql_`, `Models_` 제거
- [x] `yesung/mdd/settings.legacy.json` 제거
- [x] `yesung/src/Yesung.MainServer/Entities/`, `YesungDbContext.cs`, `Controllers/BankAccountsController.cs` 수기 stub 제거
- [x] `yesung/src/Yesung.MainServer`가 iyu-framework-v5 composite 소비 (ProjectReference + ApiRegistration 호출)
- [ ] `yesung/src/Yesung.Database/dbo/Tables_/` 레거시 수동 파일 — 여전히 남아 있으나 생성물과 공존 가능 (파일명이 다른 폴더). 완전 제거는 사용자 판단.

### 5.5 E2E 검증 (세 번째 run에서 일부 완료)
- [x] Yesung.MainServer 부팅, $metadata 200 (14 EntitySets)
- [x] POST 201 BankAccount + Enterprise with IyuTimestampInterceptor auto-populate
- [x] GraphQL schema 14 query fields 노출
- [x] nullable lookup C# 타입 일치 (`EnterpriseExt.DefaultBankName: string?`)
- [ ] `/$data/Orders?$filter=Status eq 'Confirmed'&$expand=OrderItems` — SQL Server 운영 경로에서 재검증 필요 (InMemory 제공자는 view projection 미지원)
- [ ] 수동 partial class 확장 → 재생성 → 보존 확인 — 사용자 작업
- [ ] M3L 한 필드 수정 → `mdd build` → DB/Model/API 3 프로젝트 일관 갱신 — 시각적 확인 완료

---

## 횡단 관심사 (어느 플랜에 종속되지 않음)

### 생성기 품질
- [ ] `SemanticAnalyzer` 확장 — `@lookup` 경로 무결성 (대상 엔티티/필드 존재 검증), 순환 참조 탐지, enum 값 검증, `@reference` 대상 검증
- [ ] 에러 메시지에 M3L 소스 위치(파일:라인:컬럼) 포함 — `FieldNode.Loc` 활용
- [ ] `mdd lint` 커맨드 — 생성 전에 AST/의미 검증만 수행
- [ ] `mdd watch` 커맨드 (선택) — 파일 변경 시 자동 재빌드
- [ ] 다중 `sources` 지원 검증 (Plan 1에서는 단일 파일만 테스트)

### 배포/인프라
- [ ] `.github/workflows/` — mdd-booster CI (빌드/테스트)
- [ ] NuGet 패키지 배포 전략 (선택) — 다른 소비 앱이 `dotnet tool install -g MddBooster.Cli`로 쓸 수 있게
- [ ] `D:\lib\mdd-booster\` 배포 자동화 스크립트 (`scripts/deploy.ps1`)

### 문서
- [ ] `D:\data\mdd-booster\README.md` 리라이트 — 현재는 레거시 설명
- [ ] `mdd.json` 스키마 문서 (JSON Schema 파일 + README)
- [ ] `_gen` 폴더 규약 사용자 가이드 (수동 partial class 추가 방법)

### Plan 1 잔여 메모
- `MddBooster.Core/Generation/GeneratedFile.cs`는 MVP에서 미사용 — Plan 3/4 생성기가 소비할 때 실제 쓰임새 발생
- `mdd-booster` 저장소에 `stash@{0}: legacy WIP before rewrite` 존재 — 레거시 파일이 삭제되어 pop 불가. 필요 시 `git stash drop stash@{0}`
- M3L.Native 동작 특이점:
  - 정수 타입 파라미터를 JSON float로 직렬화 (`string(30)` → `"30.0"`) — `ColumnRenderer.NumberToString`에서 보정 중
  - 인터페이스 상속 필드를 `model.Fields`에 이미 flatten (inherited-first) — `InterfaceResolver`에서 own-first로 재정렬

### 차후 (플랜 밖)
- [ ] TypeScript 타입 생성기 — 사용자 결정: "나중. 필요할 때 추가"
- [ ] PostgreSQL/MySQL 등 타 DB 타깃 지원 (M3L은 platform-independent이므로 가능하지만 현재 범위 밖)
- [ ] Migration 전략 — 레거시 DB 데이터 이관은 별도 과제
- [ ] 인증/인가 정책 — iyu-framework-v5는 `[Authorize]` 어노테이션 지원만, 구체 정책은 소비 앱 결정
