# Namespace: test.numeric

## Item

- id: identifier @pk @generated
- qty: integer @not_null "수량"
- price: decimal(12,2)? "단가"
- byte_size: long? "크기(bytes)"
- rank: short? "순위"
- flag: byte? "플래그"
