# Namespace: x

## EnterpriseChannelDefault

- id: identifier @pk @generated
- enterprise_id: identifier?
- channel: string?
- part: string

### Indexes
- ix_scope: @index(enterprise_id, channel)
