# Aspect fixture

설비 한 행에 딸리는 선택적 정비 정보. 키는 자기 것이 아니다.

## Asset
- id: identifier @pk @generated
- name: string(100)

## AssetMaintenanceProfile ::aspect(Asset)
- level: integer?
- note: string(200)?
