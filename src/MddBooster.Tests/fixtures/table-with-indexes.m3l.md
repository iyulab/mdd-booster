# Namespace: x

## Timestampable ::interface

- created_at: timestamp = now()
- updated_at: timestamp = now()

## Order : Timestampable

> 주문 — Sections.Indexes directive 검증용 fixture.

- id: identifier @pk @generated
- part: string(20)
- season: integer
- original_number: string(20)
- customer_id: identifier
- status: string(20)

- @unique(part, season, original_number)
- @index(customer_id)
- @index(status, season)
