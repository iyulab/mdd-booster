# Namespace: x

## Timestampable ::interface

- created_at: timestamp = now()
- updated_at: timestamp = now()

## Order : Timestampable

> 라벨드 인덱스 형식(`idx_xxx: @index(col)`) — M3L.Native 0.5.5+ 지원.

- id: identifier @pk @generated
- customer_id: identifier
- status: string(20)
- season: integer

### Indexes
- idx_customer: @index(customer_id)
- idx_status_season: @index(status, season)
- idx_unique: @unique(customer_id, season)
