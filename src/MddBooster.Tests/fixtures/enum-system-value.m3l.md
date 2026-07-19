# Namespace: test.payments

## PaymentMethod ::enum
- cash: "현금"
- card: "카드"
- legacy_carryover: "레거시 이관 정리" @system

## Priority ::enum
- low: "낮음"
- high: "높음"

## Payment

- id: identifier @pk @generated
- amount: decimal(18, 2) @not_null "금액"
- method: PaymentMethod @not_null "결제수단"
- priority: Priority? "우선순위"
