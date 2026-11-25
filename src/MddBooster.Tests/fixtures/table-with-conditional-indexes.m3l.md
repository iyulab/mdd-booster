# Namespace: x

## Report

> Conditional indexes — a scheduled report is unique per type and period, ad-hoc ones are not.

- id: identifier @pk @generated
- type: string(20)
- period_from: date
- source: string(20)
- owner_code: string(20)?
- is_archived: boolean = false

### Indexes
- ux_report_schedule: @unique(type, period_from, where: "source = 'SCHEDULE'")
- ux_report_owner: @unique(owner_code, type, where: "is_archived = false")
- ix_report_live: @index(type, where: "source IN ('SCHEDULE', 'MANUAL') AND is_archived = false")
- ix_report_recent
  - fields: [period_from]
  - where: "owner_code IS NOT NULL"
