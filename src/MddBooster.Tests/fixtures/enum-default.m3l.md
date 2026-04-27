# Namespace: x

## Status ::enum

> 상태값.

- draft: "작성중"
- published: "게시됨"

## Item

> enum default emit 검증용.

- id: identifier @pk @generated
- status: Status = "draft" "현재 상태"
- code: string(20) = "TBD" "임시 코드"
