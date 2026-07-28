# Namespace: test.edges

## Edge

- id: identifier @pk @generated
- zero_len: string(0)? "0 길이 — 파서가 받아들인다"
- sized: string(50)? "정상 상한"
- unsized: string? "파라미터 없음"
- not_a_string: decimal(12,2)? "문자열 아님"
- deep_scale: decimal(38,10)? "SQL Server 상한 스케일"
- one_param: decimal(12)? "파라미터 1개 — SQL은 DECIMAL(12,0)"
