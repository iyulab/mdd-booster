# Namespace: test.views

## Asset

- id: identifier @pk @generated
- name: string(100) @not_null

## FailureEvent

- id: identifier @pk @generated
- asset_id: identifier @reference(Asset) @not_null

## VAssetFailureStats ::view

### Source
- from: Asset
- group_by: [Asset.id, Asset.name]

- asset_name: string @from(Asset.name)
- failure_count: integer @rollup(FailureEvent.asset_id, count)
