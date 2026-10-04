# Namespace: probe.manytoone

## AssetLifecycleState

- id: identifier @pk @generated
- code: string(20) @not_null "코드"
- name: string(100) @not_null "상태명"

---

## Organization

- id: identifier @pk @generated
- name: string(200) @not_null "조직명"

---

## Asset

> 일반 참조(N:1) 키마다 읽기 타입에 정방향 navigation 이 생긴다. 이름은 키에서 `_id` 를 뗀 것이다.

- id: identifier @pk @generated
- name: string(200) @not_null "자산명"
- lifecycle_state_id: identifier? @reference(AssetLifecycleState) "상태"
- owner_id: identifier @not_null @reference(Organization) "소유 조직"
- parent_id: identifier? @reference(Asset) "상위 자산"
- operator_id: identifier? @reference(Organization) @internal "운영 조직(읽기 표면 밖)"
- vendor_id: identifier? @reference(Organization) "공급사"
- vendor: string @lookup(vendor_id.name) "공급사명 — navigation 이름 'Vendor' 를 먼저 차지한다"

---

## AssetMaintenanceProfile

> 같은 대상에 1:1 과 N:1 이 함께 있는 의존 모델 — 관례가 짝을 고르지 못하는 모양이다.

- id: identifier @pk @generated
- asset_id: identifier @not_null @reference(Asset) @unique "자산"
- replaced_asset_id: identifier? @reference(Asset) "교체된 자산"
