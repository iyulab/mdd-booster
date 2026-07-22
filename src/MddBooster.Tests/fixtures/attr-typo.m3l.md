# Namespace: test.attrtypo

## Timestampable ::interface

- created_at: timestamp = now()
- updated_at: timestamp = now()

---

## Warehouse : Timestampable

- id: identifier @pk @generated
- code: string(20) @uniqe "창고 코드"
- region: string(30) @tenant_scope "지역"
