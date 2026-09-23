# Namespace: test.sharedpk

## Timestampable ::interface

- created_at: timestamp = now()
- updated_at: timestamp = now()

---

## Asset : Timestampable

- id: identifier @pk @generated
- name: string(100) @not_null "자산명"

## AssetCriticality : Timestampable

- id: identifier @pk @generated
- name: string(50) @not_null

## AssetMaintenanceProfile ::aspect(Asset) : Timestampable

> 공유 PK 1:1 확장 테이블 — `::aspect(Asset)` 가 키를 만들고 그 키가 곧 부모(Asset) FK다.
> SQL 타깃: `[AssetId] ... PRIMARY KEY REFERENCES [dbo].[Asset]([Id])`, `Id` 컬럼 없음.
> Model 타깃: `IyuEntity.Id`가 `AssetId` 컬럼에 매핑되어야 한다 (fluent HasColumnName).

- criticality_id: identifier? @reference(AssetCriticality)
