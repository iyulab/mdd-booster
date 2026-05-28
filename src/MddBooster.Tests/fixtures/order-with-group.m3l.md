# Namespace: test.group

## Priority ::enum

- low: "낮음"
- high: "높음"

## OrderItem

- id: identifier @pk @generated
- name: string(50) @not_null @group("기본") "품목명"
- qty: integer @not_null @group("기본") "수량"
- priority: Priority @not_null @group("상세") "우선순위"
- note: text? @group("기타") "메모"
- created_at: timestamp
- updated_at: timestamp
