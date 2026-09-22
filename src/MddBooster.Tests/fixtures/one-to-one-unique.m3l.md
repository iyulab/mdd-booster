# Namespace: probe.onetoone

## Timestampable ::interface

- created_at: timestamp = now()
- updated_at: timestamp = now()

---

## Asset : Timestampable

- id: identifier @pk @generated
- name: string(200) @not_null "자산명"

---

## Organization : Timestampable

- id: identifier @pk @generated
- name: string(200) @not_null "조직명"

---

## AssetMaintenanceProfile : Timestampable

> 대상 이름으로 시작하는 의존 모델 — 역방향 이름은 접두를 뗀 나머지가 된다.

- id: identifier @pk @generated
- asset_id: identifier @not_null @reference(Asset) @unique "자산"
- interval_days: integer @not_null "점검 주기(일)"

---

## Contractor : Timestampable

> 대상 이름으로 시작하지 «않는» 의존 모델 — 역방향 이름은 모델명 전체다. 그리고 FK 가 nullable 이라
> 선택적 1:1 이다.

- id: identifier @pk @generated
- organization_id: identifier? @reference(Organization) @unique "조직"
- rate: decimal(10,2) @not_null "단가"
