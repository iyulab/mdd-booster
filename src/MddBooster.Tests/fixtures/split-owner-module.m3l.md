# Namespace: test.split
# Prefix: sv

> 소유자 분할 픽스처(모듈) — `SvGrade` 는 모듈 소유 enum, `SvSurvey.kind` 는 베이스 enum 참조.

## SvGrade ::enum

- a: "A"
- b: "B"

## SvSurvey

- id: identifier @pk @generated
- vessel_id: identifier @reference(Vessel) @not_null "선박"
- kind: VesselKind @not_null "선종"
- grade: SvGrade @not_null "등급"
