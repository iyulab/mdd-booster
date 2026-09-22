# Namespace: probe.onetooneclash

> 같은 대상에 1:1 참조가 둘 — 역방향 이름이 겹쳐 빌드가 멈춰야 하는 형태.

## Timestampable ::interface

- created_at: timestamp = now()
- updated_at: timestamp = now()

---

## Asset : Timestampable

- id: identifier @pk @generated
- name: string(200) @not_null "자산명"

---

## AssetProfile : Timestampable

- id: identifier @pk @generated
- primary_asset_id: identifier @not_null @reference(Asset) @unique "주 자산"
- backup_asset_id: identifier? @reference(Asset) @unique "예비 자산"
