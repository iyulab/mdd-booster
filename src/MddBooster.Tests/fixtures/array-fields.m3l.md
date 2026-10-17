# Namespace: arrays

## ArrayHolder
> A model whose fields cover every element type an array is generated for, and each nullability.

- id: identifier @pk @generated
- title: string(100)
- tags: string[]
- notes: text?[]
- scores: integer[]?
- totals: long[]
- ranks: short?[]?
- flags: boolean[]
- prices: decimal(10,2)[]
- ratios: float[]
- weights: double[]
- related_ids: identifier[]
- dates: date[]
- stamps: timestamp[]?
- secret_codes: string[] @internal
- created_at: timestamp
- updated_at: timestamp
