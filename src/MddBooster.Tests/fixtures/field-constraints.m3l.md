# Namespace: test.constraints

## Grade ::enum

> 기본값 방출 검증용 enum.

- in_review: "검토중"
- approved: "승인됨"

## Sample

> 제약 방출 검증용 — 널 허용 축 · 길이 상한 축 · 선언된 기본값 축을 함께 덮는다.

- id: identifier @pk @generated
- created_at: timestamp = now()
- updated_at: timestamp = now()

- bare_name: string(50) "속성 없이 선언된 비-널"
- explicit_name: string(60) @not_null "명시 @not_null — bare_name 과 같아야 한다"
- opt_name: string(70)? "널 허용이지만 길이 상한은 있다"
- unbounded: string "길이 상한 없음"
- memo: text "text 는 상한을 갖지 않는다"
- payload: json "참조형"
- blob: binary "참조형 — 문자열이 아니다"
- thumbnail: binary(1048576) "문자열이 아닌 길이 상한 — 컬럼은 VARBINARY(n)"

- contact_email: email "선언에 (n)이 없지만 타입이 상한을 갖는다"
- contact_phone: phone? "널 허용 + 암묵 상한"
- home_page: url? "암묵 상한 — 가장 넓은 축"

- code: string(10) = "NEW" "비-널 + 선언된 기본값"
- opt_code: string(10)? = "OPT" "널 허용 + 선언된 기본값"

- is_active: boolean = true "boolean 기본값"
- qty: integer = 3 "정수 기본값"
- ratio: decimal(5,2) = 0.5 "decimal — m 접미가 필요하다"
- weight: float = 1.5 "float — f 접미가 필요하다"
- score: double = 2.5 "double — 접미 없음"

- grade: Grade = "in_review" "enum 기본값"
- plain_grade: Grade "기본값 없는 enum"

- made_at: timestamp = now() "C# 리터럴이 없는 타입 — 건너뛴다"
- owner_id: identifier "값형 — Required 대상이 아니다"

- locked_note: text? @immutable "immutable 방출 검증 — 상한 없는 널허용 필드로 다른 축과 겹치지 않는다"

## IndexSample

> `@unique`/`@index` → Model 타깃 `HasIndex()` 방출 검증(cycle-93). `Sample`과 분리 — 대조를
> 위한 무속성 필드(`plain_ref`)를 섞어 두려면 별도 모델이 더 읽기 쉽다.

- id: identifier @pk @generated @index "PK 위에 index 겹침 — 스킵되어야 한다(PK가 이미 유일 인덱스)"
- email: string(80) @not_null @unique "unique만"
- optional_code: string(20)? @unique "nullable + unique — SQL Server는 filtered index, PG는 평범한 UNIQUE. 두 경우 모두 이 렌더러는 같은 한 줄만 방출한다(EF 자체 컨벤션이 SQL Server 필터를 붙인다)"
- tag: string(30) @index "index만"
- serial_no: string(40) @not_null @unique @index "동시 선언 — unique만 방출되어야 한다(제약이 이미 인덱스를 소유)"
- plain_ref: string(20)? "대조군 — 어느 축도 없다"
