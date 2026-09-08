# Namespace: test.formcontract

> **래칫 픽스처 — 설계된 것이다. 필드를 줄이지 말 것.**
>
> `Everything` 한 엔티티가 생성 폼이 낼 수 있는 **모든 프롭**을 띄운다. 하나라도 빠지면
> `FormControlContractRatchetTests` 가 그 프롭을 보지 못한 채 통과한다 — 래칫의 폭은 이
> 픽스처가 상한이고, 그 상한은 테스트가 스스로 단언한다(`The_fixture_emits_every_documented_prop`).
>
> | 필드 | 띄우는 프롭 |
> |---|---|
> | `title` (`string(n)` `@not_null` `@help`) | `label` `required` `description` `maxlength` `value` `onChange` |
> | `code` (`string(n)` `@immutable`) | `maxlength` (required 없이 — 선택 필드 경로) · `disabled` |
> | `body` (`text?` `@immutable` `@help`) | `minRows` · `FormRow full`(전폭 배치) · `disabled` |
> | `done` (`boolean` `@immutable` `@help`) | `checked` (그리고 `value` 부재) · `disabled` |
> | `rank` (enum `@not_null` `@help`) | `options` |
> | `mood` (enum? `@immutable`) | `placeholder` (널 허용 enum 만 낸다) · `disabled` |
> | `due` (`date`) | `type="date"` |
> | `starts_at` (`datetime`) | `type="datetime"` |
> | `amount` (`decimal(18,4)`) | `type="number"` · `step` (스케일에서 유도) |
> | `owner_id` (`@reference`) | slot 경로 — 컨트롤을 내지 않는 필드가 섞여 있어야 한다 |
> | `@group` 3개 | `FormSection title` |
>
> ⚠ `mood` 를 `@not_null` 로 바꾸면 `placeholder` 가 사라진다. `amount` 의 스케일을 지우면
> `step` 이 사라진다. 이 픽스처의 필드 속성은 **전부 load-bearing** 이다.
>
> 🔴 **`@immutable` 이 네 필드에 «흩어져» 있는 것은 실수가 아니다.** README 계약은
> *「네 컴포넌트 모두 `disabled` 를 받을 수 있어야 한다」*고 요구하는데, 한 컨트롤에서만
> 방출하면 나머지 셋에 대해서는 **아무것도 증명되지 않는다** — 실제로 `code`(string) 하나뿐이던
> 동안 `UTextarea`·`USelect`·`UCheckbox` 의 `disabled` 는 어느 검사에도 안 걸렸다
> (`The_type_gate_declares_exactly_the_props_the_generator_emits` 가 이것을 잡았다).
> 컨트롤마다 하나씩 두는 것이 이 요구를 실제로 거는 유일한 방법이다.
>
> ---
>
> 🔴 **두 번째 축 — 「비우기」 값 모양 (`@group("널 아님")`)**. 위 표는 **프롭 축**이다:
> *어떤 프롭이 방출되는가*. 그런데 프롭이 같아도 **`onChange` 가 싣는 «비우기» 값**은
> nullability 에 따라 갈린다 — nullable 이면 `null`, 아니면 `undefined`(`Partial<T>` 의 그
> 필드가 `T | undefined` 이므로 `null` 은 **타입 오류**다). 그 축은 프롭 목록에 안 나타난다.
>
> **이 축이 비어 있어서 실제 결함이 통과했다** — `text` 의 textarea 가 비우기 값으로 `null` 을
> 하드코딩하고 있었는데(형제 셋은 이미 nullability 를 따랐다), 이 픽스처의 `body` 도 상류
> acceptance 모델의 `text` 필드 6개도 **전부 nullable** 이라 어느 쪽에서도 재현되지 않았다.
> 문자열 단언 테스트 657건이 못 잡았고 `tsc` 가 잡았다.
>
> ⇒ `ClearToken` 을 쓰는 **네 컨트롤 전부**의 NOT NULL 짝을 여기 둔다. 아래 넷을 지우거나
> `?` 를 붙이면 그 컨트롤의 `undefined` 분기가 다시 검사되지 않는다.
>
> | 필드 | 고정하는 분기 |
> |---|---|
> | `summary` (`text` `@not_null`) | textarea 의 비우기 = `undefined` |
> | `due_on` (`date` `@not_null`) | date 의 비우기 = `undefined` |
> | `ends_at` (`datetime` `@not_null`) | datetime 의 비우기 = `undefined` |
> | `qty` (`decimal(18,4)` `@not_null`) | number 의 비우기 = `undefined` |

## Rank ::enum

- low: "낮음"
- high: "높음"

## Mood ::enum

- calm: "평온"
- tense: "긴장"

---

## Owner

- id: identifier @pk @generated
- name: string(50) @not_null "이름"

---

## Everything

- id: identifier @pk @generated
- owner_id: identifier @reference(Owner) "담당자"
- title: string(50) @not_null @group("기본") @help("표시용 제목") "제목"
- code: string(20) @immutable @group("기본") "코드"
- due: date @group("기본") "마감일"
- starts_at: datetime @group("기본") "시작시각"
- amount: decimal(18,4) @group("기본") "금액"
- rank: Rank @not_null @group("기본") @help("처리 우선순위") "등급"
- mood: Mood? @immutable @group("기본") "상태"
- done: boolean @not_null @immutable @group("상세") @help("완료 여부") "완료"
- body: text? @immutable @group("상세") @help("자유 서술") "본문"
- summary: text @not_null @group("널 아님") "요약"
- due_on: date @not_null @group("널 아님") "기한"
- ends_at: datetime @not_null @group("널 아님") "종료시각"
- qty: decimal(18,4) @not_null @group("널 아님") "수량"
