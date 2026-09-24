# Namespace: test.split

> 소유자 분할 픽스처(베이스) — TypeScript 게이트가 «베이스 타깃 + 모듈 타깃(공유 타입 재수출)» 을 tsc 로 컴파일한다.
> `VesselKind` 는 모듈 모델이 참조하는 베이스 enum, `VesselFlag` 는 아무 모듈도 참조하지 않는 베이스 enum 이다.

## VesselKind ::enum

- tender: "보급선"
- tug: "예인선"
- retired: "퇴역" @system

## VesselFlag ::enum

- domestic: "국내"
- foreign: "국외"

## Vessel

- id: identifier @pk @generated
- name: string(50) @not_null "선명"
- kind: VesselKind @not_null "종류"
- flag: VesselFlag? "선적"
