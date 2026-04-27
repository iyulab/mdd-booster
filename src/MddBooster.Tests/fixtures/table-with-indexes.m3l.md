# Namespace: x

## Timestampable ::interface

- created_at: timestamp = now()
- updated_at: timestamp = now()

## Order : Timestampable

> 주문 — Sections.Indexes directive 검증용 fixture.
> part/season/original_number는 nullable이라 filtered unique index가 emit되어야 함.

- id: identifier @pk @generated
- part: string(20)?
- season: integer?
- original_number: string(20)?
- customer_id: identifier
- status: string(20)

- @unique(part, season, original_number)
- @index(customer_id)
- @index(status, season)

## Membership : Timestampable

> 모든 UK 컬럼이 NOT NULL인 케이스 — inline CONSTRAINT 유지 검증.

- id: identifier @pk @generated
- user_id: identifier
- group_id: identifier

- @unique(user_id, group_id)
