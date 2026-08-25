# mdd-booster

M3L → SQL/C#/API 코드 생성기. 단일 `tables.m3l.md` 소스로 SSDT 스키마, EF Core 엔티티 + DbContext, OData/GraphQL 등록 코드를 일괄 생성한다.

> **상태**: 0.x — 표면이 아직 정착 중이며, 마이너 버전이 잘못된 동작을 고칠 수 있다.
> `dotnet tool install -g mdd` 로 설치한다([NuGet](https://www.nuget.org/packages/mdd)).
> 릴리스별 변경은 [CHANGELOG](./CHANGELOG.md) 를 볼 것.
> 이전 `MDD-Booster` 전역 도구는 이 저장소의 과거 버전이며, 현재 입력 형식·설정과 호환되지 않는다.

## 생성 타깃

| 타입 | 출력 | 소비자 |
|---|---|---|
| **Sql** | tsql(기본): `dbo/Tables_gen/{Entity}.sql`, `dbo/Views_gen/{Entity}_{full,ext}.sql`, `.sqlproj` ItemGroup 패치 · postgres: `tables_gen/{table}.sql` (snake_case, 아래 방언 절) | SSDT 프로젝트 · Schemorph |
| **Model** | `Entity_gen/{I,}{Entity}{,Ext}.cs`, `Enum_gen/{Enum}.cs`, `DbContext_gen/{Name}.cs` (with auto-`ToView` 매핑) | C# classlib (EF Core + 소비앱의 런타임 패키지) |
| **Api** | `Api_gen/ApiRegistration_gen.cs` (OData + GraphQL 엔티티 페어 등록) | ASP.NET Core 서버 (OData/GraphQL 런타임) |

## 사용법

### mdd.json (소비 프로젝트에 배치)

```json
{
  "sources": ["./tables.m3l.md"],
  "targets": [
    { "type": "Sql", "projectPath": "../src/MyApp.Database", "schema": "dbo" },
    { "type": "Model", "projectPath": "../src/MyApp.Entities", "namespace": "MyApp.Entities", "dbContextName": "MyAppDbContext" },
    { "type": "Api", "projectPath": "../src/MyApp.Server", "namespace": "MyApp.Server" }
  ]
}
```

Sql 타깃 선택 노브(모두 생략 가능): `emitSqlProj`(기본 true — SSDT `.sqlproj` 패치),
`emitRefreshScript`(기본 true — post-deployment `sp_refreshview` 스크립트),
`emitEnumCheckConstraints`(기본 false — enum 컬럼 table-level `CK_{Table}_{Column}` CHECK.
SSDT dacpac은 CHECK diff가 불안정하므로 선언형 도구(Schemorph) 소비자용 opt-in),
`emitForeignKeyIndexes`(기본 false — 아래).

**`projectPath`/`outputPath`/`formsOutputPath`/`sqlProjectPath`는 상대경로면 `mdd.json`이 있는
디렉터리 기준, 절대경로면 그대로 쓴다.** 절대경로를 쓰면서 같은 저장소를 여러 물리적
체크아웃(linked git worktree, 별도 클론)에서 빌드하면, `mdd build`를 어디서 실행하든 그
고정된 절대경로 하나에만 쓴다 — 지금 작업 중인 체크아웃과 다를 수 있다. 호출 디렉터리와
해석된 경로가 서로 다른 git 작업트리에 속하면 stderr로 경고한다(빌드는 막지 않는다).

#### TypeScript 타깃 옵션

```json
{
  "type": "TypeScript",
  "outputPath": "../ui/src/types",
  "formsOutputPath": "../ui/src/forms",
  "formLayoutImport": "@iyulab/enterprise",
  "formControlsImport": "../components/ui",
  "formSelectOptionsImport": "../lib/select-options"
}
```

| 키 | 필수 | 기본값 | 의미 |
|---|---|---|---|
| `outputPath` | ✅ | — | 생성 `*_gen.ts` 5개가 나갈 디렉터리 |
| `formsOutputPath` | | 없음 | `{Entity}Form_gen.tsx` 가 나갈 디렉터리. **생략하면 폼을 생성하지 않는다** |
| `formLayoutImport` | | `@iyulab/enterprise` | `FormSection`·`FormRow` 의 출처 |
| `formControlsImport` | | `../components/ui` | 폼 컨트롤의 출처 |
| `formSelectOptionsImport` | | `../lib/select-options` | `enumToOptions` 의 출처 |

**세 `*Import` 의 기본값은 추천이 아니라 호환을 위한 역사적 값이다.** 어느 컴포넌트
라이브러리를 가리킬지는 소비자 결정이며, 생성기는 *어떤 export 가 필요한지*까지만 규정한다
(아래 「소비 프로젝트 계약」). 새 프로젝트라면 자신의 배럴이나 라이브러리를 직접 지정하면 되고,
그러면 기본 경로에 래퍼를 만들 필요가 없다.

> **생성 타입(`entities_gen` 등)의 import 경로는 설정 항목이 아니다** — `outputPath` 와
> `formsOutputPath` 에서 **계산**된다. 두 경로를 어떻게 두든 폼은 생성된 타입을 찾는다.
> 상대경로가 성립하지 않는 조합(서로 다른 드라이브·루트)이면 **생성이 실패**하며, 컴파일되지
> 않는 파일을 써 놓고 성공을 보고하지 않는다.

#### 외래 키 인덱스 (`emitForeignKeyIndexes`)

어느 엔진도 외래 키를 자동 인덱싱하지 않는다. FK 로 하는 조인과, 삭제 시 참조 행을 확인하는
검사가 자식 테이블을 훑는다. 켜면 **모델이 덮지 않은 FK 컬럼마다** 인덱스를 만든다 —
T-SQL `IX_{Model}_{Column}` · PostgreSQL `ix_{table}_{column}`. 대상 판정은 방언과 무관하게
한 곳에서 내린다.

| 상황 | 자동 인덱스 |
|---|---|
| `@reference` 만 붙은 컬럼 | ✅ 생성 |
| `@pk` · `@unique` · `@index` 가 이미 붙음 | ❌ 제약·선언이 인덱스를 소유한다 |
| 복합 인덱스/유니크의 **선두** 컬럼 | ❌ `(a, b)` 인덱스가 `a` 조회를 처리한다 |
| 복합 인덱스/유니크의 **둘째 이후** 컬럼 | ✅ 그 인덱스로는 조회되지 않는다 |

**기본값은 `false`다.** 켜는 것은 읽기 이득과 쓰기·저장 비용의 교환이며, 기존 스키마에 조용히
적용할 판단이 아니다. 끈 상태의 산출물은 이전과 동일하다.

> 알려진 예외: **널 허용 유니크** 컬럼은 T-SQL 에서 filtered unique index 가 되어 일반 조인에는
> 쓰이지 않지만(PG 의 plain unique index 와 다르다), 판정기는 양쪽 모두 덮인 것으로 본다.
> 좁은 형태(널 허용 유니크 FK = 선택적 1:1)이고, 고치려면 판정기가 방언을 알아야 하므로 유지했다.

#### 타깃별 엔티티 부분집합 (`includeEntities` / `excludeEntities`)

한 모델 정본을 **여러 서버가 공유**할 때, 각 서버가 노출할 엔티티를 타깃별로 좁힌다.
**표면 타깃(Api·TypeScript) 전용**이며 둘 다 생략하면 전량(현행)이다.

```json
{ "type": "Api", "projectPath": "../src/MyApp.OpsServer", "namespace": "MyApp.OpsServer",
  "includeEntities": ["ProductionWork", "QRScanLog"] }
```

| 규칙 | 동작 |
|---|---|
| 둘 다 생략 / 빈 목록 | 전량 (완전 하위호환) |
| `includeEntities` | 화이트리스트 |
| `excludeEntities` | 블랙리스트 |
| 둘 다 지정 | **빌드 오류** |
| 없는 엔티티명 | **빌드 오류** + 가장 가까운 이름 제안 |
| `includeEntities` 에 `@internal` 엔티티 | **빌드 오류** (데이터 API 노출 대상이 아니다) |
| `Sql`·`Model` 타깃에 지정 | **빌드 오류** — 부분집합은 FK/상속 무결성을 깬다 |

필터가 걸린 타깃은 빌드마다 커버리지를 출력한다(`포함 N개 / 제외 M개 — 이름…`).

**Api 타깃만 좁히면 경고가 뜬다.** `entity_names_gen.ts` 의 `EntitySetName` 은 OData entity set
이름의 미러이므로, Api 표면을 좁혔는데 TypeScript 를 그대로 두면 **어떤 서버도 등록하지 않는
이름**을 계속 광고한다. 빌드가 그 이름들을 열거해 경고하며, 같은 필터를 TypeScript 타깃에도
지정하면 사라진다. 판정은 **전 Api 타깃의 합집합** 기준이므로, 공유 UI 하나가 여러 서버를
담당하는 구성(각 서버가 서로 다른 부분집합)은 경고 대상이 아니다. Api 타깃이 없는 설정
(서버를 다른 `mdd.json` 에서 생성)은 판정 근거가 없어 검사하지 않는다.

> ⚠️ **`includeEntities` 는 drift 한다.** 정본에 새 엔티티가 추가돼도 이 타깃에는 **조용히**
> 나타나지 않는다. 신규 엔티티가 기본 노출되기를 원하면 `excludeEntities` 를 쓸 것.
> (커버리지 출력이 무엇이 빠졌는지는 매 빌드에서 보여준다.)

#### 복수 타깃 게이트

같은 종류의 타깃을 **여러 개** 둘 수 있다(한 정본 → 여러 서버). 다만 조용한 오출력이 되는 두 경우는 오류다.

| 상황 | 동작 |
|---|---|
| 같은 종류·**다른** 경로 타깃 2개 | 정상 — 복수 서버 시나리오 |
| 같은 종류·**같은** 경로 타깃 2개 | **빌드 오류** (나중 것이 앞선 것의 산출물을 덮어쓴다. 경로는 정규화해 비교) |
| `Model` 타깃 1개 + `Api` 타깃 | 정상 — Api 가 그 namespace 를 추론 |
| `Model` 타깃 **2개 이상** + `entitiesNamespace` 미지정 `Api` 타깃 | **빌드 오류** — 어느 것을 참조할지 추론하지 않는다. 해당 Api 타깃에 `entitiesNamespace` 를 명시 |

```json
{ "type": "Api", "projectPath": "../src/MyApp.OpsServer", "namespace": "MyApp.OpsServer",
  "entitiesNamespace": "MyApp.Entities", "includeEntities": ["ProductionWork"] }
```

#### 생성 이름 규약

| 표면 | 규칙 | 예 (`QRScanLog`) |
|---|---|---|
| OData entity set | PascalCase 복수 (DbSet 이름과 일치) | `QRScanLogs` |
| GraphQL query / mutation prefix | camelCase (선행 약어를 묶어 소문자화 — .NET `JsonNamingPolicy.CamelCase` 와 동일) | `qrScanLogs` / `qrScanLog` |
| TypeScript 폼 헬퍼 | camelCase | `qrScanLogFromEntity` |
| PostgreSQL 식별자 | snake_case (같은 단어분리 규칙) | `qr_scan_log` |

후행 약어는 보존한다: `OrderQR` → `orderQR`.

#### `@internal` 이 영향하는 산출물

`@internal` 모델은 **데이터 API 표면에서만** 제외된다 — 테이블·뷰·C# 엔티티·EF `DbSet` 은 그대로 생성된다.

| 산출물 | `@internal` 존중 | 이유 |
|---|---|---|
| `ApiRegistration_gen.cs` · `Controllers_gen.cs` | ✅ 제외 | 범용 CRUD·시크릿 컬럼을 데이터 API에 노출하지 않는다 |
| `entity_names_gen.ts` (`EntitySetName`) | ✅ 제외 | OData entity set 이름의 미러다. 남겨 두면 존재하지 않는 경로를 타입세이프하게 광고한다 |
| `entities_gen.ts` · `field_schema_gen.ts` · `*Form_gen.tsx` | ❌ 유지 | 타입은 데이터 API 전용이 아니다 — 전용 엔드포인트로 관리되는 엔티티에도 유용하다 |
| Sql · Model 타깃 전체 | ❌ 유지 | 스토리지·EF 매핑은 노출과 무관하다 |

### PostgreSQL 방언 (`dialect: "postgres"`)

Sql·Model 타깃은 `dialect` 노브를 받는다 — 기본 `"tsql"`(위 현행 동작), `"postgres"`는
PG용 산출물을 낸다. **두 타깃에 같은 dialect를 지정할 것** (불일치 시 빌드 경고 —
DDL과 EF 매핑이 서로 다른 네이밍을 전제하게 된다).

```json
{ "type": "Sql",   "dialect": "postgres", "projectPath": "../db", "emitEnumCheckConstraints": true },
{ "type": "Model", "dialect": "postgres", "projectPath": "../src/MyApp.Entities", "namespace": "…", "dbContextName": "…" }
```

**Sql 타깃 (postgres)** — `{projectPath}/tables_gen/{table}.sql` (Schemorph desired-state
관례: 테이블당 한 파일. `schema` 기본 `public`):

- **식별자 = 비인용 snake_case**: 테이블명은 모델명 Pascal→snake 결정적 변환
  (`WorkOrder→work_order`, `FMSCode→fms_code`, `Iso14224Class→iso14224_class`),
  컬럼명은 M3L 필드명 그대로(이미 snake). **오류 게이트 4종** — 패턴
  `^[a-z][a-z0-9_]*$` · 63바이트(NAMEDATALEN, 제약명 포함 — PG의 무음 절단 차단) ·
  fold 충돌 · **PG 예약어**(`Order`, `User`, `Group` 등은 모델명으로 쓸 수 없다 —
  인용 식별자는 생성하지 않으므로 오류). 위반은 전부 모아 한 번에 보고.
- **제약은 이름 있는 제약**으로: `pk_{t}` · `fk_{t}_{col}` · `uq_{t}_{cols}` · `ck_{t}_{col}`.
  FK는 대상 모델의 **PK 물리명**을 참조하므로 공유 PK 확장 테이블 재참조가 성립
  (`REFERENCES facility_profile (facility_id)`).
- **인덱스**: `@index`/`### Indexes` 선언은 `CREATE TABLE` 뒤의 독립 문
  (`CREATE INDEX ix_{table}_{cols} ON {schema}.{table} (...)`)으로 방출한다.
  PK·UNIQUE 제약이 소유하는 인덱스는 중복 방출하지 않고, `CONCURRENTLY` 는 쓰지 않는다
  (적용이 단일 트랜잭션이라 concurrent 빌드가 참여할 수 없다). 인덱스명도 제약명과 같은
  63바이트 게이트를 통과해야 한다 — 다중 컬럼 인덱스명이 제약명보다 쉽게 넘긴다.
- **뷰는 아직 방출하지 않는다** (derived 필드의 `_full`/`_ud`) — 무음 탈락 대신 **stderr 경고**로
  표면화한다.
- 널 허용 `@unique`는 PG가 NULL을 distinct로 취급하므로 filtered index 없이 UNIQUE 제약 하나로
  정확하다 (T-SQL 경로는 filtered unique index가 필요하다).
- `emitSqlProj`/`emitRefreshScript`는 SSDT 개념 — postgres와 함께 명시하면 오류.
- 타입 매핑 주의점: `timestamp/datetime→timestamptz` · `string→text`(길이 지정 시
  `varchar(n)`) · `json→jsonb` · `byte→smallint`(PG에 1바이트 정수 없음 — 승격) ·
  `binary(n)→bytea`(**길이 상한 소실** — bytea에 길이 개념 없음).

**Model 타깃 (postgres)** — DbContext에 명시 매핑을 굽는다(런타임 네이밍 컨벤션 추론
없음): 엔티티별 `ToTable("snake")` + 저장 필드 전체 `HasColumnName("필드명")`,
공유 PK는 상속 `Id`를 PK 물리명으로, `json` 필드는 `HasColumnType("jsonb")`.
Ext 읽기 모델은 뷰 backing이 없으면 같은 테이블을 읽고, 뷰 backing이 필요한 모델
(derived 필드·soft-delete)은 **경고** — PG 방언은 뷰를 방출하지 않으므로 해당 뷰를
직접 만들기 전까지 Ext 질의는 실패한다.

### 빌드 실행

```bash
dotnet run --project src/MddBooster.Cli -- build path/to/mdd
```

또는 설치된 CLI:

```bash
mdd build ./mdd   # mdd.json 이 있는 디렉터리 — 생략하면 현재 디렉터리
```

## M3L 기능 지원 (현재)

| 기능 | 지원 |
|---|---|
| Primitive 타입 18종 (identifier/string/decimal/phone/email/...) | ✅ |
| Enum (C# enum + `[EnumMember]`; SQL CHECK는 `emitEnumCheckConstraints` opt-in) | ✅ |
| `phone`/`email`/`url` → 검증 문자열 (plain `string`, `NVARCHAR(30/320/2048)` — 상한은 언어 명세 §10.4.2가 정한다. `phone`만 명세의 20 대신 30을 쓰며 그 이탈은 코드에 기록돼 있다). **값객체 struct 매핑 아님** — `ODataConventionModelBuilder`가 값객체 struct를 EDM 복합 타입으로 등록하지 못해 직렬화가 깨지기 때문. 막힌 지점은 OData 직렬화 계층 한정이라 데이터 계층에서의 변환은 자유롭다 | ✅ |
| `@reference(Target)` → SQL FK + C# `[Reference]` 속성 | ✅ |
| `@unique` (단일 컬럼) | ✅ |
| `@lookup(fk.col)` → `_full` 뷰 LEFT JOIN + `[Lookup]` 속성 | ✅ |
| `@rollup(Target.fk, aggregate)` → `_ext` 뷰 서브쿼리 + `[Rollup]` | ✅ |
| `@computed("expr")` → `_ext` 뷰 표현식 컬럼 + `[Computed]` | ✅ |
| `@indexed` + rollup → `WITH SCHEMABINDING` | ✅ |
| `@unique(col1, col2)` 복합 | ✅ — 널 허용 컬럼이 섞이면 filtered unique index (`WHERE … IS NOT NULL`)로 방출한다. 그러지 않으면 두 번째 전체-NULL 행이 거부된다 |
| `### Indexes` 섹션 (`- @unique(...)` / `- @index(...)`) | ✅ |
| `@inherits(FQN)` → C# 베이스클래스 오버라이드 (도메인 중립, verbatim) | ✅ |
| `@implements(FQN, ...)` → C# 인터페이스 append (도메인 중립, verbatim) | ✅ |
| enum 값의 `@system` → 생성 폼 선택지에서 제외 (아래) | ✅ |
| 널 허용 여부 · `string(n)` · `= <value>` → C# 검증 어트리뷰트 + 초기화자 (아래) | ✅ |

### 선언된 제약 → C# 엔티티 (Model 타깃)

모델이 선언한 제약은 SQL 컬럼뿐 아니라 생성 엔티티에도 방출된다. API 표면이 모델이 금지한 값을
받아 데이터베이스까지 내려보내지 않도록 하기 위한 것이다.

| 선언 | 방출 | 비고 |
|---|---|---|
| 널 허용하지 않는 참조형 필드 | `[Required]` | `string`/`text`/`json`/`phone`/`email`/`url`/`binary`. 값형(숫자·시간·`Guid`·enum)은 CLR이 이미 널을 허용하지 않으므로 제외 |
| `string(n)` | `[StringLength(n)]` | **널 허용 여부와 무관** — `string(50)?` 도 상한 50을 갖는다 |
| `phone`·`email`·`url` | `[StringLength(n)]` | 상한이 **선언이 아니라 타입**에서 온다(명세 §10.4.2 — 30/320/2048). 컬럼·엔티티·필드 스키마·생성 폼이 같은 `n` 을 쓴다 |
| `= <value>` | 속성 초기화자 | `= true;` · `= 3;` · `= "NEW";` · `= 0.5m;` · `= Status.Draft;` |
| `@immutable` | `[Editable(false)]` | 저장 필드에만 붙는다. 아래 TypeScript 타깃의 생성 폼 `disabled` prop과 같은 선언을 미러링 — 둘 다 메타데이터일 뿐, 이 리포가 생성하는 어떤 API 계층도 이를 강제하지 않는다 |
| 필드 단위 `@unique` | `HasIndex(...).IsUnique()` | 엔티티 속성이 아니라 `DbContext.OnModelCreating`의 fluent 설정 — SQL 타깃의 `UK_{Model}_{Column}`/PG의 `uq_{table}_{field}`와 같은 이름을 `HasDatabaseName`으로 명시한다. 널 허용 컬럼도 분기 없이 같은 한 줄만 방출 — SQL Server 프로바이더가 unique index의 널 허용 컬럼에 `WHERE ... IS NOT NULL` 필터를 자동으로 붙이므로 (SQL 타깃의 filtered index와 동일 결과), PostgreSQL은 애초에 `UNIQUE`가 NULL을 distinct로 취급하므로 (둘 다 이 리포가 손으로 만드는 코드가 아니다) |
| 필드 단위 `@index` | `HasIndex(...)` | 위와 같은 자리, `IX_{Model}_{Column}`/`ix_{table}_{field}` 이름. `@unique`와 함께 선언되면 `unique`만 방출한다(제약이 이미 인덱스를 소유 — SQL 타깃과 동일한 배제) |

기준은 `@not_null` 을 적었는지가 **아니라 필드가 실제로 널을 허용하는지**다. `- name: string(50)`
처럼 속성 없이 선언한 필드도 컬럼이 `NOT NULL` 이므로 동일하게 방출된다.

`@unique`/`@index`는 **필드 단위 선언만** 방출한다. 섹션 레벨 복합 선언(`@unique(c1, c2)` ·
`@index(c1)`)은 아직 이 타깃에 닿지 않는다 — 별도 축이다.

**`[Required]` 는 `NOT NULL` 보다 좁다.** SQL `NOT NULL` 컬럼은 빈 문자열을 허용하지만
`[Required]` 는 거부하고, 판정 전에 trim 하므로 공백만 있는 값도 거부한다. 널 허용하지 않는
문자열 필드는 API 표면에서 "값이 있어야 한다"를 뜻하게 된다.

파생 필드(`@lookup`/`@rollup`/`@computed`)에는 `[Required]` 가 붙지 않는다 — 뷰가 채우는 값이라
호출자가 보낼 수 없고, 붙이면 생성 요청이 전부 거부된다. `[StringLength]` 역시 저장 필드에만 붙는다.

기본값은 **C# 리터럴로 표현할 수 있는 타입에만** 방출된다. 시간 타입 · `identifier` · `binary` 는
기본값 표기가 무엇이든 초기화자를 만들지 않으므로 `= now()` 같은 서버 측 기본값이 코드로 새지 않는다.
널 허용 필드는 선언된 기본값이 있어도 초기화자를 받지 않는다 — 선택 속성을 채우면 "미설정"의
의미가 바뀌기 때문이다.

### ⚠️ 소비 프로젝트 계약 (TypeScript 타깃)

생성된 `*Form_gen.tsx`는 **소비 프로젝트가 제공해야 하는 모듈**을 import한다.
**생성되는 모든 import는 소비자에 대한 요구조건이다** — 아래를 갖추지 않으면 생성 코드가
소비앱 빌드에서 컴파일되지 않는다. (mdd-booster 자체 테스트는 생성된 TS를 컴파일하지 않으므로,
이 계약의 위반은 **소비앱 빌드에서만** 드러난다.)

> **계약은 "무엇을 export 하는가"이지 "어디에 두는가"가 아니다.** 아래 각 절은
> `formControlsImport`·`formSelectOptionsImport`·`formLayoutImport` 가 **가리키는 모듈**을
> 규정한다(0.12.0부터 설정 가능 — 위 「TypeScript 타깃 옵션」). 괄호 안 경로는 그 옵션의
> **기본값**일 뿐이며, 그 경로에 파일을 두지 않아도 된다.
> 어디를 가리키든 **그 모듈이 아래 표면을 export 해야 한다는 요구는 그대로다.**

> 🔴 **이 계약은 이름 목록이 아니라 동작 계약이다.** export 이름이 맞는 것만으로는 충족되지
> 않는다 — 예컨대 `onChange` 로 **값이 아니라 이벤트 객체를 넘기는** 바인딩은 이름이 같아도
> 이 계약을 만족하지 않는다. 그런 모듈을 가리키면 TS strict 에서는 컴파일이 깨지고, 느슨한
> 설정에서는 **이벤트 객체가 폼 모델에 저장된다**(무음 데이터 오염). 그 경우 필요한 것은
> 이름을 바꾸는 재수출이 아니라 **어댑터** — 이벤트→값 변환, 배열 `options`→자식 요소 투영,
> 프롭 이름 번역처럼 실제 동작을 옮기는 계층이다. 웹 컴포넌트를 감싸는 통과형 바인딩
> (`@lit/react` 계열 등)이 대표적으로 이 경우에 해당한다.

#### `formControlsImport` 가 가리킬 모듈 — 폼 컨트롤 (기본값 `../components/ui`)

| 컴포넌트 | 언제 import되나 | 받는 프롭 |
|---|---|---|
| `UInput` | date · 숫자 · 문자열 필드 | `label` `required?` `description?` `type?`(`"date"`/`"number"`) **`step?: number`** **`maxlength?: number`** **`disabled?: boolean`** `value: string` `onChange: (v: string) => void` |
| `UTextarea` | `text` 필드 | `label` `required?` `description?` **`minRows: number`** **`disabled?: boolean`** `value: string` `onChange: (v: string) => void` |
| `USelect` | enum 필드 | `label` `required?` `description?` `placeholder?` **`disabled?: boolean`** `value: string` `options` `onChange: (v: string) => void` |
| `UCheckbox` | boolean 필드 | `label` `description?` **`disabled?: boolean`** `checked: boolean` `onChange: (v: boolean) => void` |

- `required` / `description` 은 **모델이 그렇게 말할 때만** 방출된다
  (`@not_null` → `required`, `@help("...")` → `description`).
  즉 **네 컴포넌트 모두 `description`을 받을 수 있어야 한다** — 하나라도 빠지면
  그 타입의 필드에 `@help`를 붙이는 순간 빌드가 깨진다.
- **`disabled`는 새로 추가된 요구조건이다**(버전은 [CHANGELOG](https://github.com/iyulab/mdd-booster/blob/main/CHANGELOG.md) 참조)
  — `@immutable`이 붙은 필드에서만 방출된다. 값은 여전히 채워지고(`value`/`onChange`는 그대로
  연결됨) 편집만 막힌다 — 필드를 감추는 것과는 다르다. **네 컴포넌트 모두 이 프롭을 받을 수
  있어야 한다.** `readOnly`가 아니라 `disabled`를 선택한 이유: 체크박스·셀렉트는 네이티브
  `readOnly` 의미가 컴포넌트마다 일관되지 않지만, `disabled`는 넷 다 동일하게 지원한다.
- `value`/`onChange`는 **controlled 패턴**을 전제한다(빈 상태 sentinel은 `''`).
- **`step`·`maxlength`는 0.8.0에서 추가된 요구조건이다** — 0.7.0 이하에서 만든 래퍼는
  갱신해야 한다([CHANGELOG 0.8.0](https://github.com/iyulab/mdd-booster/blob/main/CHANGELOG.md#080) 참조).
- **`step`·`maxlength`는 `number`이지 문자열이 아니다** — 생성물은 `step={0.0001}` ·
  `maxlength={50}`(중괄호 숫자 리터럴)을 방출한다. 래퍼가 이를 그대로 아래 input에 전달해야 한다.
- ⚠️ **`maxlength`는 소문자다** (React DOM의 `maxLength`가 아니라 `u-input`의 표면 표기).
  그리고 `string(n)`은 거의 모든 모델에 있으므로 **`step`보다 영향 범위가 훨씬 넓다** —
  `decimal`을 안 쓰는 소비자도 이 프롭은 거의 확실히 필요하다. 래퍼에 없으면 TS2322로 빌드가 깨진다.

#### `formSelectOptionsImport` 가 가리킬 모듈 — 헬퍼 (기본값 `../lib/select-options`)

```ts
export function enumToOptions(labels: Record<string, string>): /* USelect의 options 타입 */
```
생성기가 요구하는 것은 **인자 하나**와 그 결과가 `USelect`의 `options`에 그대로 들어간다는 것뿐이다
(반환 타입은 소비자가 정한다). 값을 좁힐 때도 **인자를 늘리지 않고** 좁혀진 라벨맵을 따로 생성해
넘기므로(아래 `@system` 절), 이 시그니처는 안정적이다.

#### `formLayoutImport` 가 가리킬 모듈 — 레이아웃 (기본값 `@iyulab/enterprise`)

`FormSection`(`title` **`className?: string`** **`style?: CSSProperties`**) · `FormRow`(`full?`) 를 export해야 한다.

- **`className`·`style`는 새로 추가된 요구조건이다**(버전은 [CHANGELOG](https://github.com/iyulab/mdd-booster/blob/main/CHANGELOG.md) 참조)
  — 생성 폼의 `{Entity}FormBase`가 받는 `sectionProps?: {Entity}FormSectionProps`(섹션 제목 →
  `{ className?, style? }`)를 그대로 각 `<FormSection>`에 전달한다. 소비앱이 특정 섹션만 접기/
  숨기기 같은 CSS 기반 커스터마이징을 스스로 구현할 수 있게 하려는 것 — 내장 접힘 시맨틱은
  아직 아니다(수요 관측 전). **`className`/`style`은 `sectionProps`를 실제로 넘기는지와 무관하게
  항상 방출된다**(`undefined`일 뿐 프롭 자체는 항상 전달됨) — `FormSection`이 이 프롭을 선언하지
  않으면 `sectionProps`를 쓰지 않는 소비앱도 **모든 생성 폼의 컴파일이 즉시 깨진다.**

#### 생성기가 제공하는 것 (소비자가 만들 필요 없음)

`../types/entities_gen` · `../types/enums_gen` · `../types/enum_labels_gen` — 전부 생성물이다.

### 타입 → 컨트롤 매핑

| m3l 타입 | 컨트롤 | 비고 |
|---|---|---|
| `text` | `UTextarea` (`minRows={3}`) | 길이 무제한 = 여러 줄 의도. SQL 타깃도 `NVARCHAR(MAX)`로 방출하며, 폼에서 **전폭 배치**된다 |
| `boolean` | `UCheckbox` | |
| enum 타입명 | `USelect` | |
| `date` | `UInput type="date"` | `DateOnly` → `"2026-07-28"`, 컨트롤이 받는 형식과 일치 |
| `timestamp` · `datetime` · `time` | `UInput` (**자유 텍스트, 의도적**) | 네이티브 피커가 **값을 파괴한다** — 아래 |
| `decimal(p,s)` | `UInput type="number" step={10^-s}` | 스케일에서 유도. `decimal(18,4)`→`step={0.0001}`. 파라미터 없는 `decimal`은 SQL 기본값 `DECIMAL(18,2)`에 맞춰 `step={0.01}` |
| 정수 타입 (`integer`/`long`/`short`/`byte`), `decimal(p,0)` | `UInput type="number"` | `step` 미방출 — HTML 기본값 1이 정확히 맞다 |
| `float` / `double` | `UInput type="number"` | **알려진 한계**: `step` 미방출이라 **소수 입력이 막힌다**. 정답인 `step="any"`를 `step?: number` 계약이 담지 못한다(아래) |
| `string(n)` | `UInput maxlength={n}` | SQL `NVARCHAR(n)`의 상한을 UI로 앞당긴다. 저장된 값은 이미 n 이내이므로 기존 값을 무효화하지 않는다 |
| `phone`·`email`·`url` | `UInput maxlength={n}` | 상한이 **선언이 아니라 타입**에서 온다(명세 §10.4.2 — 30/320/2048). `string(n)`과 같은 자리에 같은 값으로 방출된다. **길이 축 한정** — 형식 검증(`type="email"` 등)은 앱 계층 몫이라 컨트롤은 평문 입력 그대로다 |
| `string`(무파라미터) | `UInput` | `NVARCHAR(MAX)`라 상한이 없다 — 방출할 것이 없다 |
| `@reference` FK · `@slot` | **슬롯 자리표시자** | 호출부가 내용을 주입 |

> **`step`은 선택 옵션이 아니다.** `<input type="number">`의 `step` 기본값은 1이라, 없으면
> 브라우저가 소수를 거부하고("Value must be a multiple of 1") **submit이 앱에 아무 신호 없이
> 막힌다** — 오류 없이 아무 일도 안 일어나는 것처럼 보인다. 스케일은 모델이 이미 갖고 있고
> (SQL 타깃의 `DECIMAL(p,s)`, Model 타깃의 `[Column(TypeName)]`이 같은 값을 쓴다) 폼도 그것을 따른다.
>
> **`float`/`double`은 아직 이 혜택을 못 받는다.** 스케일 개념이 없어 `step="any"`가 유일한
> 정답인데, 계약상 `step`이 `number`라 `"any"`를 실을 수 없다. 소수가 필요한 필드는
> **`float`/`double` 대신 `decimal(p,s)`로 모델링할 것** — 정밀도가 명시되므로 SQL·EF·폼이
> 모두 같은 약속을 하게 된다.

> **`timestamp`/`datetime`/`time`이 자유 텍스트인 것은 미구현이 아니라 결정이다.**
> 피커는 API가 돌려주는 값을 컨트롤이 받아들일 때만 도움이 된다. 실측 결과:
>
> | m3l | CLR | JSON 직렬화 | 컨트롤이 받는 형식 | 왕복 |
> |---|---|---|---|---|
> | `date` | `DateOnly` | `"2026-07-28"` | `type="date"` = `YYYY-MM-DD` | ✅ |
> | `timestamp`·`datetime` | `DateTimeOffset` | `"2026-07-28T14:30:00+09:00"` | `datetime-local` = `YYYY-MM-DDTHH:mm[:ss]` — **오프셋 불가** | ❌ |
> | `time` | `TimeOnly` | `"14:30:45"` · `"14:30:45.1230000"` | `type="time"` — 기본 `step=60`(초 거부), 소수 초 3자리 한계 | ❌ |
>
> SQL 타입이 `DATETIMEOFFSET`·`TIME(7)`이므로 오프셋과 소수 초는 **실제 데이터**다.
> 피커를 붙이면 브라우저가 그 값을 거부해 컨트롤이 **빈 칸으로 렌더**되고, 그대로 저장하면
> **기존 값이 지워진다** — 값을 보여주기라도 하는 자유 텍스트보다 나쁘다.
> `type="time" step={1}`은 초 문제만 풀고 소수 초는 조용히 버려서 **어떤 값은 되고 어떤 값은
> 사라지는** 더 나쁜 상태를 만든다.
>
> 숫자 `step`과 방향이 반대라는 점에 주의: 거기서는 모델 정보를 컨트롤로 옮기면 막혔던 입력이
> **가능해지지만**, 여기서는 컨트롤이 모델 정보를 담지 못해 옮기면 데이터가 **사라진다**.
> 증상("모델은 타입을 아는데 폼이 안 쓴다")이 같아 보여도 처방이 반대다.
> 피커를 쓰려면 오프셋 인지 변환 계층이 소비자 계약에 추가돼야 한다 — 사람이 결정할 사안이다.

> `minRows`는 선택 옵션이 아니다. `<u-textarea>`는 자동 높이 조절이라 **1줄에서 시작**하므로,
> 없으면 단일행 입력과 육안으로 구분되지 않는다. (속성명은 `minRows`이며 `rows`는 존재하지 않는다.)

### 생성기 해석 attribute (TypeScript 타깃)

M3L 파서는 attribute를 **의미 없이 기록만** 한다. 아래는 mdd-booster의 TypeScript 타깃이
그 기록에 부여하는 의미다. M3L 표준 attribute(`@pk`·`@reference` 등)와 달리 **이 목록은
mdd-booster 고유**이며, 다른 생성기는 이들을 무시한다.

| attribute | 대상 | 생성 결과 |
|---|---|---|
| `@group("이름")` | 필드 | 폼을 `<FormSection title="이름">`으로 묶는다. 없으면 `"기타"` 섹션 |
| `@help("설명")` | 필드 | 컨트롤에 `description="설명"` — 라벨은 짧게 두고 예시·부연을 아래로 분리 |
| `@slot` | 필드 | 인라인 컨트롤 대신 **슬롯 자리표시자**로 렌더 (호출부가 내용을 주입). `@reference` FK 필드는 자동으로 슬롯 |
| `@display_labels(다른Enum)` | enum 필드 | 표시 텍스트만 다른 enum의 라벨맵으로 교체. **저장/캐스트 타입은 그대로** |
| `@system` | **enum 값** | 생성 폼 선택지에서 제외 (아래) |

### enum 값의 `@system` — 표시 라벨과 입력 선택지의 분리

M3L은 enum **값**에 붙은 attribute를 의미 없이 기록만 한다. 그 의미를 정하는 것은 생성기의 몫이며,
`@system`은 **"시스템이 쓰는 값, 사람이 고르는 값이 아니다"** 로 해석된다.

```markdown
## PaymentMethod ::enum
- cash: "현금"
- card: "카드"
- legacy_carryover: "레거시 이관 정리" @system
```

생성 결과:

```ts
// enum_labels_gen.ts — 표시 라벨은 전체 유지 (기존 행이 계속 렌더돼야 하므로)
export const PaymentMethodLabels: Record<PaymentMethod, string> = {
  Cash: '현금', Card: '카드', LegacyCarryover: '레거시 이관 정리',
} as const

/** Input choices — excludes values marked @system in the model. */
export const PaymentMethodSelectableLabels: Record<Exclude<PaymentMethod, 'LegacyCarryover'>, string> = {
  Cash: '현금', Card: '카드',
} as const
```

```tsx
// {Entity}Form_gen.tsx — 폼은 좁혀진 맵을 쓴다
options={enumToOptions(PaymentMethodSelectableLabels)}
```

**`@system`은 저장이 아니라 작성(authoring)을 제한한다.** 값은 SQL CHECK 제약(opt-in 시),
C# enum, 표시 라벨 맵에 **그대로 남는다** — 서버·마이그레이션이 쓰는 유효한 저장값이기 때문이다.
빠지는 것은 생성 폼의 선택지뿐이다.

소비앱의 `enumToOptions` 헬퍼 시그니처는 **바뀌지 않는다**. 좁힘은 별도 맵으로 표현되므로
헬퍼는 여전히 `Record<string, string>` 하나만 받으면 된다.

> 요구 버전: `M3L.Native` 0.6.0 이상 (enum 값 attribute 파싱 지원).

## 생성물 구조

```
consumer-repo/
├── mdd/
│   ├── mdd.json
│   └── tables.m3l.md
└── src/
    ├── MyApp.Database/
    │   ├── MyApp.Database.sqlproj  (patched)
    │   └── dbo/
    │       ├── Tables_gen/         ← 매번 재생성
    │       │   ├── User.sql
    │       │   └── Order.sql
    │       └── Views_gen/          ← 매번 재생성 (derived 필드 있는 모델만)
    │           ├── Order_full.sql
    │           └── Order_ext.sql
    ├── MyApp.Entities/
    │   ├── Entity_gen/             ← 매번 재생성
    │   ├── Enum_gen/
    │   └── DbContext_gen/
    └── MyApp.Server/
        └── Api_gen/
            └── ApiRegistration_gen.cs
```

생성된 partial 클래스는 같은 네임스페이스의 수동 확장을 지원한다. `_gen` 접미 폴더는 재생성 시 완전히 덮어써지므로 절대 수동 편집하지 말 것.

## 의미 분석 (SemanticAnalyzer)

빌드 전에 cross-entity 무결성 검사:

| 코드 | 의미 |
|---|---|
| MDD001 | 필드 타입이 primitive/enum/model 어느 것도 아님 |
| MDD002 | `@reference(X)` 대상 엔티티 없음 |
| MDD003 | `@lookup` 경로가 `fk.col` 형태가 아님 |
| MDD004 | `@lookup(fk.col)`의 fk가 동일 모델에 없음 |
| MDD005 | 해당 fk에 `@reference` 없음 |
| MDD006 | lookup target 엔티티에 `col` 필드 없음 |
| MDD007-9 | `@rollup` 대응 검증 |

에러 발생 시 exitcode 3으로 종료.

## 프로젝트 구조

```
src/
├── MddBooster.Core/              AST 로딩, Semantic 분석, 공용 naming/primitives
├── MddBooster.Generators.Sql/    TableRenderer, ViewPlanner, Full/ExtViewRenderer, SqlProjPatcher
├── MddBooster.Generators.Model/  CSharpTypeMapper, EnumRenderer, EntityPairRenderer, DbContextRenderer
├── MddBooster.Generators.Api/    ApiRegistrationRenderer (OData + GraphQL)
├── MddBooster.Cli/               BuildCommand (mdd.json 소비)
└── MddBooster.Tests/             467 xUnit tests (Roslyn 구문/의미 검증 포함)
```

## 테스트 실행

```bash
dotnet test MddBooster.slnx --nologo
```

테스트 커버리지:
- Renderer 단위 (E2E Roslyn 구문 검증)
- Semantic analyzer cross-entity 검증
- CLI 3-타깃 통합 E2E (임시 디렉터리에서 전체 파이프라인 실행)
- **acceptance 게이트** — 기능 행렬을 한 번에 교차하는 대규모 모델(24 엔티티 + 15 enum)로
  4-타깃 빌드를 돌리고, 산출물을 **모델이 선언한 것과 이름 단위로 대조**한다.
  기대값은 파싱한 모델에서 도출되므로 엔티티를 늘려도 테스트의 숫자를 고칠 필요가 없고,
  산출물이 빠진 선언은 개수 차이가 아니라 이름으로 보고된다.

  모델은 리포에 체크인돼 있어 CI·신규 클론에서도 그대로 돈다.
  `MDDBOOSTER_ACCEPTANCE_MODEL` 에 다른 `.m3l.md` 경로를 넣으면 같은 게이트를 그 모델로
  돌린다(릴리스 전 대규모 모델 점검용). 경로가 잘못되면 **건너뛰지 않고 실패**한다.

## 문서

- [설계 원칙](docs/design-principles.md) — 이 프로젝트가 왜 지금의 경계를 긋는가
- [기여하기](CONTRIBUTING.md) — 개발 환경, acceptance 게이트, 타깃 축 비대칭 체크

## 관련 저장소

- [m3l](https://github.com/iyulab/m3l) — Rust 기반 M3L 파서 (NuGet `M3L.Native`)
- [iyu-framework-v5](https://github.com/iyulab/iyu-framework-v5) — 런타임 (EF Core + OData + HotChocolate GraphQL)
