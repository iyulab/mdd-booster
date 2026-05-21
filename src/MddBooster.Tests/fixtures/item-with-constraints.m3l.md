# Namespace: test.item

## Item

- id: identifier @pk @generated
- name: string(50)
- qty: integer @min(1)
- rate: decimal(5,2) = 0 @min(0) @max(100)
- notes: text?
