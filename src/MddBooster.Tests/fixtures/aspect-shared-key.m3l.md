# Namespace: test.aspect

설비 한 행에 딸리는 선택적 정비 정보. 키는 자기 것이 아니다.

## Timestampable ::interface
- created_at: timestamp = now()
- updated_at: timestamp = now()

---

## Asset : Timestampable
- id: identifier @pk @generated
- name: string(100)

## AssetMaintenanceProfile ::aspect(Asset) : Timestampable
- level: integer?
- note: string(200)?
