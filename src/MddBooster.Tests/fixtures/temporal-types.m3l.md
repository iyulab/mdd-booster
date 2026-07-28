# Namespace: test.temporal

## Event

- id: identifier @pk @generated
- happened_on: date? "발생일"
- happened_at: timestamp? "발생시각"
- scheduled_at: datetime? "예정시각"
- opens_at: time? "개시시각"
