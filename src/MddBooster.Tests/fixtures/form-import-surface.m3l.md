# Namespace: test.formimports

> **래칫 픽스처 — 설계된 것이다. 필드를 줄이지 말 것.**
>
> `Ticket` 한 엔티티가 생성 폼이 낼 수 있는 **모든 import 종류**를 띄운다. 하나라도 빠지면
> `FormImportSurfaceRatchetTests` 가 그 종류를 보지 못한 채 통과한다 — 래칫의 폭은 이
> 픽스처가 상한이다.
>
> | 필드 | 띄우는 import |
> |---|---|
> | `owner_id` (`@reference`) | `react` (`ReactNode` — slot 타입) |
> | `title` (`string(n)`) | 컨트롤 (`UInput`) |
> | `body` (`text?`) | 컨트롤 (`UTextarea`) |
> | `done` (`boolean`) | 컨트롤 (`UCheckbox`) |
> | `priority` (enum) | 컨트롤 (`USelect`) · `enums_gen` · `enum_labels_gen` · 옵션 헬퍼 |
> | (엔티티 자체) | `entities_gen` · 레이아웃 |

## Priority ::enum

- low: "낮음"
- high: "높음"

---

## Owner

- id: identifier @pk @generated
- name: string(50) @not_null "이름"

---

## Ticket

- id: identifier @pk @generated
- owner_id: identifier @reference(Owner) "담당자"
- title: string(50) @not_null "제목"
- body: text? "본문"
- done: boolean @not_null "완료"
- priority: Priority @not_null "우선순위"
