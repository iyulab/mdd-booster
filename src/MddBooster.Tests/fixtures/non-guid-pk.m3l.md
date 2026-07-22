# Namespace: test.nonguidpk

## CodeKeyed

> 비-identifier 타입 PK — SQL 타깃은 렌더 가능하나 Model 타깃은 `IyuEntity.Id`(Guid)와
> 양립 불가하므로 명시적 빌드 오류여야 한다 (조용한 삼킴 금지).

- code: string(20) @pk
- name: string(50) @not_null
