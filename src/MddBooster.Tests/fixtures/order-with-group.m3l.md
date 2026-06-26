# Namespace: test.group

## Priority ::enum

- low: "낮음"
- high: "높음"

## Mode ::enum

- on: "켜짐"
- off: "꺼짐"

## OrderItem

- id: identifier @pk @generated
- name: string(50) @not_null @group("기본") "품목명"
- qty: integer @not_null @group("기본") "수량"
- priority: Priority @not_null @group("상세") "우선순위"
- priority_alt: Priority @not_null @group("상세") @display_labels(AltPriority) "대체 우선순위"
- mode: Mode @not_null @group("상세") @display_labels(AltMode) "모드"
- note: text? @group("기타") "메모"
- created_at: timestamp
- updated_at: timestamp
