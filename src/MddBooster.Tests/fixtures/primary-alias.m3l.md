# Namespace: test.primaryalias

## Sample

> `@primary` 별칭 픽스처 — 스펙 §10.8.1은 `@pk`를 `@primary`의 별칭으로 선언한다.
> 두 표기는 모든 생성기에서 동일하게 동작해야 한다.

- id: identifier @primary @generated
- name: string(100) @not_null "이름"
- note: string(200)? "비고"
