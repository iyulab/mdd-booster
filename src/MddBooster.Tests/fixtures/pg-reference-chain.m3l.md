# Namespace: test.pgchain

## Facility

- id: identifier @pk @generated
- name: string(50) @not_null

## FacilityProfile

> 공유 PK 1:1 확장 — PK 필드가 곧 부모 FK. PG 방언은 참조 PK를 대상 모델의
> **PK 물리명**으로 렌더해야 한다 (`[Id]` 하드코딩 제거 검증 — 이 테이블을 재참조하는
> FacilityInspection의 FK는 `facility_profile (facility_id)`를 가리켜야 한다).

- facility_id: identifier @pk @reference(Facility)
- grade: string(10)?

## FacilityInspection

> field-level `@index` · section-level `@index`는 PG 방언에서 방출 금지 대상
> (Schemorph PG P1이 인덱스를 명시 거부) — 무음 탈락이 아니라 경고로 표면화한다.
> nullable `@unique`는 PG에선 filtered index 없이 UNIQUE 제약이 정확한 모델링
> (PG는 NULL을 distinct로 취급).

- id: identifier @pk @generated
- facility_profile_id: identifier @reference(FacilityProfile)
- status: string(20) @index
- serial_no: string(30)? @unique

- @index(facility_profile_id)
