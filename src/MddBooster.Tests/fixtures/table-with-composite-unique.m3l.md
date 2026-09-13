# Namespace: x

## EnterpriseChannelDefault

- id: identifier @pk @generated
- enterprise_id: identifier?
- channel: string?
- part: string

### Indexes
- ux_scope: @unique(enterprise_id, channel, part)
