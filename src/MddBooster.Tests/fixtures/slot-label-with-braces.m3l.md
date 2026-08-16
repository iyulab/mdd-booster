# Namespace: test.slotlabel

## Category

- id: identifier @pk @generated
- name: string(50) @not_null "이름"

---

## Item

- id: identifier @pk @generated
- category_id: identifier @reference(Category) "분류 목록 [{a, b}]"
