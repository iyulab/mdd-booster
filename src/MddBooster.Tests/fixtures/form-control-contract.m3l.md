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
> | `code` (`string(n)`) | `maxlength` (required 없이 — 선택 필드 경로) |
> | `body` (`text?` `@help`) | `minRows` · `FormRow full`(전폭 배치) |
> | `done` (`boolean` `@help`) | `checked` (그리고 `value` 부재) |
> | `rank` (enum `@not_null` `@help`) | `options` |
> | `mood` (enum?) | `placeholder` (널 허용 enum 만 낸다) |
> | `due` (`date`) | `type="date"` |
> | `amount` (`decimal(18,4)`) | `type="number"` · `step` (스케일에서 유도) |
> | `owner_id` (`@reference`) | slot 경로 — 컨트롤을 내지 않는 필드가 섞여 있어야 한다 |
> | `@group` 2개 | `FormSection title` |
>
> ⚠ `mood` 를 `@not_null` 로 바꾸면 `placeholder` 가 사라진다. `amount` 의 스케일을 지우면
> `step` 이 사라진다. 이 픽스처의 필드 속성은 **전부 load-bearing** 이다.

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
- code: string(20) @group("기본") "코드"
- due: date @group("기본") "마감일"
- amount: decimal(18,4) @group("기본") "금액"
- rank: Rank @not_null @group("기본") @help("처리 우선순위") "등급"
- mood: Mood? @group("기본") "상태"
- done: boolean @not_null @group("상세") @help("완료 여부") "완료"
- body: text? @group("상세") @help("자유 서술") "본문"
