# mdd-booster

M3L → SQL/C#/API 코드 생성기. 단일 `tables.m3l.md` 소스로 SSDT 스키마, EF Core 엔티티 + DbContext, OData/GraphQL 등록 코드를 일괄 생성한다.

> **상태**: 리라이트 중 (2026-04-05). 이전 `MDD-Booster` 전역 도구는 이 저장소의 과거 버전. 현재 구조는 4-저장소 스택 (`m3l` / `mdd-booster` / `iyu-framework-v5` / consumer) 일부로 재설계됨. 자세한 배경: [`claudedocs/plans/2026-04-05-mdd-booster-rewrite-design.md`](claudedocs/plans/2026-04-05-mdd-booster-rewrite-design.md).

## 생성 타깃

| 타입 | 출력 | 소비자 |
|---|---|---|
| **Sql** | `dbo/Tables_gen/{Entity}.sql`, `dbo/Views_gen/{Entity}_{full,ext}.sql`, `.sqlproj` ItemGroup 패치 | SSDT 프로젝트 |
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
| Enum (C# enum + `[EnumMember]` + SQL CHECK) | ✅ |
| Value Object (`phone`/`email`/`url` → `Iyu.Core.ValueObjects.*`) | ✅ |
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

**`@system`은 저장이 아니라 작성(authoring)을 제한한다.** 값은 SQL CHECK 제약, C# enum,
표시 라벨 맵에 **그대로 남는다** — 서버·마이그레이션이 쓰는 유효한 저장값이기 때문이다.
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

## 설계 문서

- [`claudedocs/plans/2026-04-05-mdd-booster-rewrite-design.md`](claudedocs/plans/2026-04-05-mdd-booster-rewrite-design.md) — 정본 설계 스펙
- [`claudedocs/cycle-logs/`](claudedocs/cycle-logs/) — 사이클별 개발 이력 (local, .gitignore)
- [`TASKS.md`](TASKS.md) — Plan 단위 완료/잔여 추적
