# Namespace: test.nullableref

## Customer

- id: identifier @pk @generated
- name: string(50) @not_null "이름"

---

## Order

> nullable FK 테스트 픽스처

- id: identifier @pk @generated
- customer_id: identifier? @reference(Customer) "고객 (선택)"
- order_no: string(20) @not_null "주문번호"
