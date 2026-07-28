# Changelog

이 파일은 **0.8.0부터** 기록한다. 그 이전 릴리스의 내용은 git 태그(`v0.3.0` ~ `v0.7.0`)와
커밋 이력을 참조할 것 — 사후에 재구성해 적으면 실제와 어긋날 수 있어 소급하지 않았다.

버전 정책: `0.MINOR.PATCH`. MINOR는 기능 추가, PATCH는 수정.

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
