# Namespace: test.specconformance

## Item

> 스펙 §10.8.1 정합 픽스처 — `@index`(DB 인덱스)와 `@default(value)`(`= value`의 대안 표기).
> 2026-07-22 실측: 둘 다 무경고로 탈락하고 있었다 (silent).

- id: identifier @pk @generated
- sku: string(30) @index "SKU"
- status: string(20) @default("active")
- qty: integer = 0
