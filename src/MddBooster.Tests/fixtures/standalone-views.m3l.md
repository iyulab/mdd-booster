# Namespace: test.views

> The derived-view examples of the M3L specification (§4.7.2–4.7.6), over the models they read.
> `ViewResolverTests` resolves them; the SQL target renders the same file.

## Customer

- id: identifier @pk @generated
- name: string(100) @not_null "이름"
- email: string(200)? "이메일"

## Product

- id: identifier @pk @generated
- name: string(100) @not_null "이름"
- price: decimal(10,2) @not_null "가격"
- stock_quantity: integer @not_null "재고"
- is_active: boolean @not_null "판매 중"

## Order

- id: identifier @pk @generated
- customer_id: identifier @reference(Customer) "고객"
- order_number: string(30) @not_null "주문번호"
- ordered_at: timestamp @not_null "주문시각"
- status: string(20) @not_null "상태"
- total_amount: decimal(12,2) @not_null "합계"

## OrderItem

- id: identifier @pk @generated
- order_id: identifier @reference(Order) "주문"
- product_id: identifier @reference(Product) "상품"
- quantity: integer @not_null "수량"
- unit_price: decimal(10,2) @not_null "단가"
- subtotal: decimal(12,2) @not_null "소계"

## ActiveProducts ::view

> Active products available for sale

### Source
- from: Product
- where: "is_active = true AND stock_quantity > 0"
- order_by: name asc

- id: identifier @from(Product.id)
- name: string @from(Product.name)
- price: decimal(10,2) @from(Product.price)
- stock: integer @from(Product.stock_quantity)

## OrderDetail ::view

> Order detail information - combines order, customer, and item data

### Source
- from: Order
- join: Customer on Order.customer_id = Customer.id
- join: OrderItem on Order.id = OrderItem.order_id
- join: Product on OrderItem.product_id = Product.id

- order_number: string @from(Order.order_number)
- ordered_at: timestamp @from(Order.ordered_at)
- status: string @from(Order.status)
- customer_name: string @from(Customer.name)
- customer_email: string @from(Customer.email)
- product_name: string @from(Product.name)
- quantity: integer @from(OrderItem.quantity)
- unit_price: decimal(10,2) @from(OrderItem.unit_price)
- subtotal: decimal(12,2) @from(OrderItem.subtotal)

## CustomerStats ::view

> Per-customer order statistics

### Source
- from: Customer
- group_by: [Customer.id, Customer.name, Customer.email]

- customer_name: string @from(Customer.name)
- email: string @from(Customer.email)
- total_orders: integer @rollup(Order.customer_id, count)
- total_spent: decimal(12,2) @rollup(Order.customer_id, sum(total_amount))
- last_order: timestamp? @rollup(Order.customer_id, max(ordered_at))

## BigSpenders ::view

> Nested over another view, without expressions

### Source
- from: CustomerStats

- customer_name: string @from(CustomerStats.customer_name)
- total_spent: decimal(12,2) @from(CustomerStats.total_spent)
