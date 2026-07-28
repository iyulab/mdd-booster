# Namespace: test.numeric

## Item

- id: identifier @pk @generated
- qty: integer @not_null "수량"
- price: decimal(12,2)? "단가"
- byte_size: long? "크기(bytes)"
- rank: short? "순위"
- flag: byte? "플래그"
- precise: decimal(18,4)? "정밀값"
- bare: decimal? "무파라미터"
- whole: decimal(10,0)? "정수 스케일"
- ratio: double? "비율"
