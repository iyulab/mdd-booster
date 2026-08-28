# Namespace: samples.tasktracker

## TaskStatus ::enum
- todo: "할 일"
- in_progress: "진행중"
- done: "완료"

## Timestampable ::interface
- created_at: timestamp = now()
- updated_at: timestamp = now()

---

## Project : Timestampable

> 작업을 묶는 상위 단위.

- id: identifier @pk @generated
- code: string(20) @not_null @unique "프로젝트 코드"
- name: string(80) @not_null "프로젝트명"

- task_count: integer @rollup(Task.project_id, count) "작업 수"
- total_estimated_hours: decimal(12,2) @rollup(Task.project_id, sum(estimated_hours)) @indexed "예상 총 시간"

## Task : Timestampable

> 프로젝트에 속한 개별 작업.

- id: identifier @pk @generated
- project_id: identifier @reference(Project) @not_null
- title: string(120) @not_null "제목"
- status: TaskStatus @not_null = todo
- estimated_hours: decimal(6,2) @not_null "예상 시간"
- buffer_hours: decimal(6,2) @not_null = 0 "버퍼 시간"

- project_name: string @lookup(project_id.name) "프로젝트명"
- planned_hours: decimal(6,2) @computed(`estimated_hours + buffer_hours`) "계획 시간"
