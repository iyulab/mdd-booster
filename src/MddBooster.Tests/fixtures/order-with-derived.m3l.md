# Namespace: test.orders

## OrderStatus ::enum
- draft: "작성중"
- confirmed: "확정"
- shipped: "배송됨"

## Customer
- id: identifier @pk @generated
- name: string(50) @not_null
- email: string(100) @not_null

## Order
- id: identifier @pk @generated
- order_number: string(30) @not_null @unique
- customer_id: identifier @reference(Customer) @not_null
- status: OrderStatus @not_null
- subtotal: decimal(12,2) @not_null

- customer_name: string @lookup(customer_id.name) "고객명"
- customer_email: string @lookup(customer_id.email)
- item_count: integer @rollup(OrderItem.order_id, count)
- total_sum: decimal(12,2) @rollup(OrderItem.order_id, sum(line_total)) @indexed
- tax_amount: decimal(12,2) @computed(`subtotal * 0.1`)
- grand_total: decimal(12,2) @computed(`subtotal + tax_amount`)

## OrderItem
- id: identifier @pk @generated
- order_id: identifier @reference(Order) @not_null
- line_total: decimal(12,2) @not_null
