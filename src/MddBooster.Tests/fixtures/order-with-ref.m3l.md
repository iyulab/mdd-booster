# Namespace: test.orderref

## Customer

- id: identifier @pk @generated
- name: string(50) @not_null "이름"

---

## Order

> FK 테스트 픽스처

- id: identifier @pk @generated
- customer_id: identifier @reference(Customer) "고객"
- order_no: string(20) @not_null "주문번호"
- note: string(200)?
