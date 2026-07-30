# Changelog

이 파일은 **0.8.0부터** 기록한다. 그 이전 릴리스의 내용은 git 태그(`v0.3.0` ~ `v0.7.0`)와
커밋 이력을 참조할 것 — 사후에 재구성해 적으면 실제와 어긋날 수 있어 소급하지 않았다.

버전 정책: `0.MINOR.PATCH`. MINOR는 기능 추가, PATCH는 수정.

---

## 0.9.0

### ⚠️ 소비자 조치 필요 — 이름이 바뀐다 (대문자 약어로 시작하는 엔티티가 있는 경우)

엔티티명이 **대문자 약어로 시작하면**(`QRScanLog`, `QRCode`) 지금까지 camelCase 변환이
첫 글자만 소문자화해 어색한 이름을 만들었다. `.NET JsonNamingPolicy.CamelCase`와 같은
표준 규칙으로 정정했다:

| 표면 | before | after |
|---|---|---|
| GraphQL query / mutation | `("qRScanLogs", "qRScanLog")` | **`("qrScanLogs", "qrScanLog")`** |
| 생성 폼 헬퍼 (`*Form_gen.tsx`) | `export function qRScanLogFromEntity` | **`export function qrScanLogFromEntity`** |
| OData entity set | `"QRScanLogs"` | `"QRScanLogs"` — **변경 없음** (DbSet 이름과 맞춤) |

후행 약어는 보존한다: `OrderQR` → `orderQR`.

**재생성만으로 회수되지 않는 곳이 하나 있다 — 손으로 쓴 `AddEntityPair` 호출부.**
생성물 밖에서 `options.GraphQL.AddEntityPair<…>("qRScanLogs", "qRScanLog")` 를 직접 적어 둔
컴포지션 코드가 있으면 **컴파일 오류가 나지 않는다** — 그 서버만 옛 필드명을 계속 노출하고,
재생성된 다른 서버는 새 필드명을 노출해 **같은 엔티티가 서버마다 다른 이름으로 보인다**.
업그레이드 시 `AddEntityPair(` 를 검색해 손으로 적은 인자를 함께 고칠 것.

기존 GraphQL 쿼리가 옛 필드명을 참조하고 있으면 그 쿼리도 함께 갱신해야 한다.
(영향 판정 방법: 모델 정본에서 `^[A-Z]{2,}` 로 시작하는 엔티티가 없으면 이 릴리스는 무영향이다.)

### ⚠️ 소비자 조치 필요 — `EntitySetName` union 이 좁아진다 (`@internal` 모델이 있는 경우)

`entity_names_gen.ts` 의 `ENTITY_NAMES`/`EntitySetName` 에서 **`@internal` 모델이 빠진다.**

이 목록은 OData entity set 이름의 미러이고 소비자가 이걸로 OData URL 을 만든다. 그런데
`@internal` 모델은 `AddEntityPair` 가 등록하지 않으므로 **그 이름들은 존재하지 않는 경로였다** —
타입세이프하게 404 를 광고하고 있었던 셈이다. 결함 수정이다.

`EntitySetName` 을 좁히므로, 그 이름을 쓰던 코드가 있으면 컴파일 오류가 난다(그 경로는
애초에 동작하지 않았다). **`entities_gen.ts` 의 인터페이스·`*Form_gen.tsx` 는 그대로 유지된다** —
타입은 데이터 API 전용이 아니고 전용 엔드포인트로 관리되는 엔티티에도 유용하기 때문이다.

### 추가 — 타깃별 엔티티 부분집합 (`includeEntities` / `excludeEntities`)

한 모델 정본을 여러 서버가 공유할 때, **표면 타깃(Api·TypeScript)** 이 노출할 엔티티를
타깃별로 좁힐 수 있다. 둘 다 생략하면 전량 — **완전 하위호환**이다.

```json
{ "type": "Api", "projectPath": "../src/MyApp.MesServer", "namespace": "MyApp.MesServer",
  "includeEntities": ["ProductionWork", "QRScanLog"] }
```

- 둘을 함께 지정 · 없는 엔티티명(가장 가까운 이름 제안 포함) · `includeEntities` 에 `@internal`
  모델 · `Sql`/`Model` 타깃에 지정 — **전부 빌드 오류**다. 조용히 무시하거나 부분 적용하지 않는다.
- 필터가 걸린 타깃은 빌드마다 커버리지를 출력한다(`포함 N개 / 제외 M개 — 이름…`).
- ⚠️ `includeEntities` 는 drift 한다 — 정본의 새 엔티티가 이 타깃에는 조용히 안 나타난다.
  신규 엔티티 기본 노출을 원하면 `excludeEntities` 를 쓸 것.

전체 규칙표는 README "타깃별 엔티티 부분집합" 절 참조.

### 수정 — 복수 타깃의 조용한 오출력 2건이 빌드 오류로 올라갔다

한 정본을 여러 서버가 소비하는 구성에서 **빌드는 성공하는데 산출물이 틀린** 두 경우가 있었다.
둘 다 소비자가 컴파일 오류나 죽은 라우트를 만난 뒤에야 원인을 찾게 되는 형태였다.

- **`Model` 타깃이 2개 이상일 때** `Api` 타깃이 **첫 번째** Model 의 namespace 를 집고 나머지를
  조용히 무시했다 → 잘못된 `using` 방출. 이제 후보가 둘 이상이면 추론하지 않고 빌드 오류로
  **`entitiesNamespace` 명시를 요구**한다. 신규 옵션 `entitiesNamespace` (Api 타깃, 생략 시 종전대로 추론).
  Model 타깃이 하나인 기존 구성은 **무영향**이다.
- **같은 종류·같은 경로 타깃이 2개**면 나중 것이 앞선 것의 산출물을 조용히 덮어썼다 → 빌드 오류.
  경로는 정규화해 비교하므로 `../api` 와 `../x/../api` 도 중복으로 잡힌다.
  같은 종류라도 **경로가 다르면 정상**이다(복수 서버 시나리오).

### 내부 — 단어분리 규칙이 한 곳으로 모였다

PascalCase 단어분리(ADR-0001 §2.1)가 snake_case 경로에만 있고 camelCase 경로 두 곳은
각자 손구현 사본을 갖고 있어서 `QRScanLog`가 `qr_scan_log`(정상)와 `qRScanLog`(훼손)로
갈라졌다. `MddBooster.Core.Naming.NameCasing`이 분리기의 정본이 되고 두 사본을 제거했다.
GraphQL 복수형은 이제 **camelCase를 먼저 적용한 뒤 복수화**한다 — 순서가 반대면 두 글자
약어가 `QR` → `QRs` → `qRs`로 깨진다.

---

## 0.8.0

### ⚠️ 소비자 조치 필요 (TypeScript 타깃을 쓰는 경우)

생성 폼(`*Form_gen.tsx`)이 **새 프롭 2종을 방출**한다. 소비 프로젝트의
`../components/ui` 배럴에 있는 `UInput` 래퍼가 이들을 받아 아래 input으로 전달해야 하며,
**없으면 재생성 후 `tsc`가 TS2322로 실패한다.**

```ts
export function UInput(props: {
  label: string
  value: string
  onChange: (v: string) => void
  required?: boolean
  type?: string
  description?: string
  step?: number        // ← 0.8.0 신규. decimal 필드가 있는 모델
  maxlength?: number   // ← 0.8.0 신규. string(n) 필드가 있는 모델
}) { /* 아래 input으로 그대로 전달 */ }
```

- **`maxlength`가 더 넓게 영향한다.** `decimal`은 안 쓸 수 있어도 `string(n)`은 거의 모든
  모델에 있다. `decimal`을 쓰지 않는 소비자도 이 프롭은 거의 확실히 필요하다.
- 표기 주의: **소문자 `maxlength`** (React DOM의 `maxLength` 아님), 두 값 모두 **`number`**
  (생성물은 `step={0.0001}` · `maxlength={50}` 형태의 중괄호 숫자 리터럴을 방출한다).
- `description?`은 0.8.0 신규가 아니라 **기존 계약**이다. 래퍼에 없다면 어떤 필드든
  `@help("...")`를 붙이는 순간 같은 방식으로 깨진다 — 이번 기회에 함께 갖출 것.

전체 계약은 README의 "소비 프로젝트 계약 (TypeScript 타깃)" 절 참조.

### 추가 — 모델 타입 정보가 입력 어포던스로 전달된다

생성 폼이 모델의 타입 정보를 버리고 있던 지점을 메웠다. SQL 타깃(`DECIMAL(p,s)`,
`NVARCHAR(n)`)과 Model 타깃(`[Column(TypeName)]`)은 같은 정보를 이미 소비하고 있었고,
TypeScript 폼 타깃만 버리고 있었다.

- **`decimal(p,s)` → `step={10^-s}`** — `decimal(18,4)`는 `step={0.0001}`.
  이전에는 `step`이 없어 브라우저 기본값 1이 적용됐고, **소수 입력이 필요한 필드는 저장이
  불가능**했다("Value must be a multiple of 1"). 오류 없이 submit이 조용히 막히는 형태라
  앱에는 아무 신호도 가지 않았다.
  - 파라미터 없는 `decimal`은 `step={0.01}` (SQL 기본값 `DECIMAL(18,2)`와 일치).
  - 정수 타입과 `decimal(p,0)`은 `step`을 방출하지 않는다 — HTML 기본값 1이 정확히 맞다.
- **`string(n)` → `maxlength={n}`** — `NVARCHAR(n)`의 상한을 UI로 앞당긴다.
  저장된 값은 이미 n 이내이므로 기존 값을 무효화하지 않는다. 상한이 없는 무파라미터 `string`과
  `text`(둘 다 `NVARCHAR(MAX)`)는 방출하지 않는다.

### 알려진 한계 (의도적 미구현 — 문서화)

- **`float`/`double`은 여전히 소수 입력이 막힌다.** 스케일 개념이 없어 `step="any"`가 유일한
  정답인데, 소비자 계약상 `step`이 `number`라 `"any"`를 실을 수 없다.
  **소수가 필요한 필드는 `float`/`double` 대신 `decimal(p,s)`로 모델링할 것** —
  정밀도가 명시되므로 SQL·EF·폼이 모두 같은 약속을 하게 된다.
- **`timestamp`/`datetime`/`time`은 자유 텍스트 입력으로 남는다 — 이것은 결정이다.**
  `DateTimeOffset`이 직렬화하는 `"2026-07-28T14:30:00+09:00"`을 `<input type="datetime-local">`이
  받지 못하고(오프셋 불가), `TimeOnly`의 소수 초를 `<input type="time">`이 받지 못한다
  (기본 `step=60`, 소수 3자리 한계). 피커를 붙이면 브라우저가 값을 거부해 컨트롤이 **빈 칸**이
  되고 그대로 저장하면 **기존 값이 지워진다** — 값을 보여주기라도 하는 자유 텍스트보다 나쁘다.
  `date`만 피커를 받는 이유도 이것이다(`DateOnly` → `"2026-07-28"`은 컨트롤이 그대로 받는다).

### 내부

- `FieldAttributes.TypeParams` / `FieldAttributes.StringMaxLength`를 `MddBooster.Core`의
  정본으로 세우고, `EntityPairRenderer`·`TsFieldSchemaRenderer`가 각자 갖고 있던 동등 로직을
  제거했다. 같은 컬럼의 정밀도·길이를 두 곳이 각자 계산하던 상태를 해소.
