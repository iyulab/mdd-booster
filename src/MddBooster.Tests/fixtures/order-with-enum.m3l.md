# Namespace: test.orders

## OrderStatus ::enum
- draft: "작성중"
- confirmed: "확정"
- in_production: "생산중"
- shipped: "배송됨"
- cancelled: "취소됨"

## Priority ::enum

> 주문 우선순위

- low: "낮음"
- normal: "보통"
- high: "높음"

## Order

- id: identifier @pk @generated
- order_number: string(30) @not_null @unique "주문번호"
- status: OrderStatus @not_null "상태"
- priority: Priority? "우선순위"
- notes: text? "메모"
