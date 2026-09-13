# Namespace: x

## EnterpriseChannelDefault

> Fallback/override table — a NULL in `channel` or `part` means "applies to the whole
> remaining range" and must itself compete for uniqueness, not be excluded from it.

- id: identifier @pk @generated
- enterprise_id: identifier?
- channel: string?
- part: string

### Indexes
- ux_scope: @unique(enterprise_id, channel, part, nulls: "not_distinct")
