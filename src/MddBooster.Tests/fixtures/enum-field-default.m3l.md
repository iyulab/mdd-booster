# Namespace: test.enumdefault

## Status ::enum

- draft: "초안"
- in_production: "생산중"

## Widget

- id: identifier @pk @generated
- status: Status @not_null @default(draft) "상태"
