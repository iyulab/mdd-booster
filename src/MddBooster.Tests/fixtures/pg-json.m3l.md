# Namespace: test.pgjson

## ApiLog

> PG 방언에서 `json` 필드는 DDL이 `jsonb` 컬럼이므로(EF Npgsql은 string 속성을
> 기본 text로 매핑) 생성 DbContext에 `HasColumnType("jsonb")` 명시 매핑이 필요하다.

- id: identifier @pk @generated
- payload: json
- note: string(100)?
