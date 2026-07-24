# mdd-booster

M3L → SQL/C#/API 코드 생성기. 단일 `tables.m3l.md` 소스로 SSDT 스키마, EF Core 엔티티 + DbContext, OData/GraphQL 등록 코드를 일괄 생성한다.

> **상태**: 리라이트 중 (2026-04-05). 이전 `MDD-Booster` 전역 도구는 이 저장소의 과거 버전. 현재 구조는 4-저장소 스택 (`m3l` / `mdd-booster` / `iyu-framework-v5` / consumer) 일부로 재설계됨.

## 생성 타깃

| 타입 | 출력 | 소비자 |
|---|---|---|
| **Sql** | tsql(기본): `dbo/Tables_gen/{Entity}.sql`, `dbo/Views_gen/{Entity}_{full,ext}.sql`, `.sqlproj` ItemGroup 패치 · postgres: `tables_gen/{table}.sql` (snake_case, 아래 방언 절) | SSDT 프로젝트 · Schemorph |
| **Model** | `Entity_gen/{I,}{Entity}{,Ext}.cs`, `Enum_gen/{Enum}.cs`, `DbContext_gen/{Name}.cs` (with auto-`ToView` 매핑) | C# classlib (EF Core + Iyu.Core) |
| **Api** | `Api_gen/ApiRegistration_gen.cs` (OData + GraphQL 엔티티 페어 등록) | ASP.NET Core MainServer (iyu-framework-v5) |

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
SSDT dacpac은 CHECK diff가 불안정하므로 선언형 도구(Schemorph) 소비자용 opt-in).

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
- **범위는 Schemorph PG P1과 정렬** (테이블·컬럼·제약): `@index`/`### Indexes`의 인덱스와
  뷰(derived 필드의 `_full`/`_ud`)는 **방출하지 않고 stderr 경고**로 표면화 —
  Schemorph가 인덱스·뷰를 지원하는 슬라이스(P2/P3)에서 재개. nullable `@unique`는 PG가
  NULL을 distinct로 취급하므로 filtered index 없이 UNIQUE 제약 하나로 정확하다.
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
cd path/to/mdd
dotnet run --project D:/data/mdd-booster/src/MddBooster.Cli -- .
```

또는 배포된 `mdd.exe`:

```bash
mdd ./mdd  # mdd.json이 있는 디렉터리
```

## M3L 기능 지원 (현재)

| 기능 | 지원 |
|---|---|
| Primitive 타입 18종 (identifier/string/decimal/phone/email/...) | ✅ |
| Enum (C# enum + `[EnumMember]`; SQL CHECK는 `emitEnumCheckConstraints` opt-in) | ✅ |
| `phone`/`email`/`url` → 검증 문자열 (plain `string`, `NVARCHAR(30/200/500)`). **값객체 매핑 아님** — `ODataConventionModelBuilder`가 값객체 struct를 EDM 복합 타입으로 등록하지 못해 직렬화가 깨지기 때문. 데이터 계층(`Iyu.Data`)엔 값객체 `ValueConverter`가 있으므로 막힌 지점은 OData 직렬화 계층 한정 | ✅ |
| `@reference(Target)` → SQL FK + C# `[Reference]` 속성 | ✅ |
| `@unique` (단일 컬럼) | ✅ |
| `@lookup(fk.col)` → `_full` 뷰 LEFT JOIN + `[Lookup]` 속성 | ✅ |
| `@rollup(Target.fk, aggregate)` → `_ext` 뷰 서브쿼리 + `[Rollup]` | ✅ |
| `@computed("expr")` → `_ext` 뷰 표현식 컬럼 + `[Computed]` | ✅ |
| `@indexed` + rollup → `WITH SCHEMABINDING` | ✅ |
| `@unique(col1, col2)` 복합 | ⏳ |
| `### Indexes` 섹션 | ⏳ |
| `@inherits(FQN)` → C# 베이스클래스 오버라이드 (도메인 중립, verbatim) | ✅ |
| `@implements(FQN, ...)` → C# 인터페이스 append (도메인 중립, verbatim) | ✅ |
| enum 값의 `@system` → 생성 폼 선택지에서 제외 (아래) | ✅ |

### ⚠️ 소비 프로젝트 계약 (TypeScript 타깃)

생성된 `*Form_gen.tsx`는 **소비 프로젝트가 제공해야 하는 모듈**을 import한다.
**생성되는 모든 import는 소비자에 대한 요구조건이다** — 아래를 갖추지 않으면 생성 코드가
소비앱 빌드에서 컴파일되지 않는다. (mdd-booster 자체 테스트는 생성된 TS를 컴파일하지 않으므로,
이 계약의 위반은 **소비앱 빌드에서만** 드러난다.)

#### `../components/ui` — 배럴이 export해야 하는 컴포넌트

| 컴포넌트 | 언제 import되나 | 받는 프롭 |
|---|---|---|
| `UInput` | date · 숫자 · 문자열 필드 | `label` `required?` `description?` `type?`(`"date"`/`"number"`) `value: string` `onChange: (v: string) => void` |
| `UTextarea` | `text` 필드 | `label` `required?` `description?` **`minRows: number`** `value: string` `onChange: (v: string) => void` |
| `USelect` | enum 필드 | `label` `required?` `description?` `placeholder?` `value: string` `options` `onChange: (v: string) => void` |
| `UCheckbox` | boolean 필드 | `label` `description?` `checked: boolean` `onChange: (v: boolean) => void` |

- `required` / `description` 은 **모델이 그렇게 말할 때만** 방출된다
  (`@not_null` → `required`, `@help("...")` → `description`).
  즉 **네 컴포넌트 모두 `description`을 받을 수 있어야 한다** — 하나라도 빠지면
  그 타입의 필드에 `@help`를 붙이는 순간 빌드가 깨진다.
- `value`/`onChange`는 **controlled 패턴**을 전제한다(빈 상태 sentinel은 `''`).

#### `../lib/select-options` — 헬퍼

```ts
export function enumToOptions(labels: Record<string, string>): /* USelect의 options 타입 */
```
생성기가 요구하는 것은 **인자 하나**와 그 결과가 `USelect`의 `options`에 그대로 들어간다는 것뿐이다
(반환 타입은 소비자가 정한다). 값을 좁힐 때도 **인자를 늘리지 않고** 좁혀진 라벨맵을 따로 생성해
넘기므로(아래 `@system` 절), 이 시그니처는 안정적이다.

#### `@iyulab/enterprise` — 레이아웃

`FormSection`(`title`) · `FormRow`(`full?`) 를 export해야 한다.

#### 생성기가 제공하는 것 (소비자가 만들 필요 없음)

`../types/entities_gen` · `../types/enums_gen` · `../types/enum_labels_gen` — 전부 생성물이다.

### 타입 → 컨트롤 매핑

| m3l 타입 | 컨트롤 | 비고 |
|---|---|---|
| `text` | `UTextarea` (`minRows={3}`) | 길이 무제한 = 여러 줄 의도. SQL 타깃도 `NVARCHAR(MAX)`로 방출하며, 폼에서 **전폭 배치**된다 |
| `boolean` | `UCheckbox` | |
| enum 타입명 | `USelect` | |
| `date` | `UInput type="date"` | |
| 숫자 타입 (`integer`/`decimal`/`long`/…) | `UInput type="number"` | |
| 그 외 (`string(n)` 등) | `UInput` | |
| `@reference` FK · `@slot` | **슬롯 자리표시자** | 호출부가 내용을 주입 |

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
└── MddBooster.Tests/             126+ xUnit tests (Roslyn 구문 검증 포함)
```

## 테스트 실행

```bash
dotnet test MddBooster.slnx --nologo
```

테스트 커버리지:
- Renderer 단위 (E2E Roslyn 구문 검증)
- Semantic analyzer cross-entity 검증
- CLI 3-타깃 통합 E2E (임시 디렉터리에서 전체 파이프라인 실행)
- **Yesung 실제 14엔티티 + 13 enum acceptance test**

## 관련 저장소

- [m3l](https://github.com/iyulab/m3l) — Rust 기반 M3L 파서 (NuGet `M3L.Native`)
- [iyu-framework-v5](https://github.com/iyulab/iyu-framework-v5) — 런타임 (EF Core + OData + HotChocolate GraphQL)
- yesung — 첫 소비 애플리케이션 (예성카렌다 OMS)
