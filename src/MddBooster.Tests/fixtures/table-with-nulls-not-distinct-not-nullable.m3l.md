# Namespace: x

## EnterpriseChannelDefault

> `nulls: "not_distinct"` with no nullable column in the constraint — SQL Server's own
> convention never applies a filter here in the first place, so `.HasFilter(null)` must
> not be emitted (it would just be redundant noise on top of `.IsUnique()`).

- id: identifier @pk @generated
- enterprise_id: identifier
- channel: string
- part: string

### Indexes
- ux_scope: @unique(enterprise_id, channel, part, nulls: "not_distinct")
