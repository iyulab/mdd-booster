# Namespace: test.docs

## Memo

- id: identifier @pk @generated
- body: text? @help("여러 줄로 작성하세요") "본문"

## Article

- id: identifier @pk @generated
- title: string(200) @not_null "제목"
- content: text? "내용"
- summary: text @not_null "요약"
