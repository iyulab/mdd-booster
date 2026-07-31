# Namespace: sample.operations

<!--
  Acceptance fixture — a single model that crosses the whole feature matrix at
  scale, so one build exercises every generator path together rather than one
  at a time.

  It is designed, not copied. Three shapes below are deliberate regressions
  kept alive because each one produced a whole-model failure that no
  single-feature fixture reproduced:

  1. An enum description spanning several lines whose continuation contains
     text that is valid C# if the `///` prefix is dropped (`v3+: ...` parses as
     identifier and operators). One such enum yielded dozens of compile errors.
  2. A composite unique declaration over nullable columns — without a filtered
     index the second all-NULL row is rejected.
  3. A nullable 64-bit column, which regressed to a non-nullable mapping once.

  Keep those shapes when editing. Growing the model is fine; flattening it is
  what makes the gate stop earning its runtime.
-->

## Timestampable ::interface

- created_at: timestamp = now()
- updated_at: timestamp = now()

---

## AssetStatus ::enum

> 설비의 수명주기 상태.
> v3+: 폐기(retired)와 매각(disposed)을 구분한다. 이전 판은 둘을 하나로 두었다.
> 마이그레이션은 disposed -> retired 로 접어서 읽는다.

- planned: "도입예정"
- operating: "가동중"
- suspended: "중지"
- retired: "폐기"
- disposed: "매각"

## WorkOrderState ::enum

> 작업지시 진행 상태.
> a && b 형태의 텍스트가 설명문에 들어갈 수 있다.

- draft: "작성중"
- approved: "승인됨"
- in_progress: "진행중"
- on_hold: "보류"
- completed: "완료"
- cancelled: "취소"

## Priority ::enum

- low: "낮음"
- normal: "보통"
- high: "높음"
- urgent: "긴급"

## PriorityShortLabel ::enum

> Priority 와 멤버가 같고 표기만 다른 라벨 집합 — @display_labels 대상.

- low: "하"
- normal: "중"
- high: "상"
- urgent: "긴급"

## MaintenanceKind ::enum

- preventive: "예방"
- corrective: "사후"
- predictive: "예지"
- statutory: "법정"

## PartCategory ::enum

- mechanical: "기계"
- electrical: "전기"
- consumable: "소모품"
- lubricant: "윤활유"

## MeasurementUnit ::enum

- each: "개"
- meter: "m"
- kilogram: "kg"
- liter: "L"
- hour: "시간"

## ApprovalDecision ::enum

- pending: "대기"
- granted: "승인"
- rejected: "반려"

## ShiftName ::enum

- day: "주간"
- swing: "오후"
- night: "야간"

## DocumentKind ::enum

- manual: "매뉴얼"
- drawing: "도면"
- certificate: "인증서"
- photo: "사진"

## NotificationChannel ::enum

- email: "이메일"
- sms: "문자"
- push: "푸시"

## SeverityLevel ::enum

- info: "정보"
- warning: "경고"
- critical: "심각"

## InspectionResult ::enum

- pass: "적합"
- conditional: "조건부"
- fail: "부적합"

## ContractType ::enum

- spot: "단건"
- annual: "연간"
- framework: "기본계약"

## CurrencyCode ::enum

- krw: "원"
- usd: "달러"
- eur: "유로"

---

## Site : Timestampable

> 사업장 — 참조 체인의 뿌리.

- id: identifier @pk @generated
- code: string(20) @not_null @unique "사업장 코드"
- name: string(80) @not_null "사업장명"
- address: string(200)? "주소"
- contact_email: email? "대표 메일"
- homepage: url? "홈페이지"
- opened_on: date? "개소일"
- is_active: boolean = true "운영 여부"

## Building : Timestampable

- id: identifier @pk @generated
- site_id: identifier @reference(Site) @not_null
- name: string(60) @not_null "건물명"
- floors_above: short? "지상 층수"
- floors_below: short? "지하 층수"
- site_name: string @lookup(site_id.name) "사업장명"

- @index(site_id)

## Floor : Timestampable

- id: identifier @pk @generated
- building_id: identifier @reference(Building) @not_null
- level: integer @not_null "층 번호 — 지하는 음수"
- usable_area: decimal(10,2)? "사용면적"

- @unique(building_id, level)

## Department : Timestampable

- id: identifier @pk @generated
- code: string(20) @not_null @unique
- name: string(60) @not_null
- parent_id: identifier? @reference(Department) "상위 부서 — 자기참조"

## Employee : Timestampable

- id: identifier @pk @generated
- department_id: identifier @reference(Department) @not_null
- employee_no: string(20) @not_null @unique "사번"
- name: string(40) @not_null @group("기본") "성명"
- work_email: email? @group("연락처") "업무 메일"
- mobile: phone? @group("연락처") "휴대전화"
- hired_on: date? @group("기본") "입사일"
- profile: json? "확장 속성"
- department_name: string @lookup(department_id.name) "부서명"

- @index(department_id)

## ShiftPattern : Timestampable

- id: identifier @pk @generated @index "PK 위에 인덱스를 겹쳐 선언 — 위와 같은 계열"
- shift: ShiftName @not_null "근무조"
- starts_at: time @not_null "시작시각"
- ends_at: time @not_null "종료시각"
- note: text? "비고"

## Asset : Timestampable

> 설비 — 이 모델의 중심. lookup/rollup/computed 를 모두 걸어 둔다.

- id: identifier @pk @generated
- floor_id: identifier @reference(Floor) @not_null
- department_id: identifier? @reference(Department) "관리 부서"
- tag: string(40) @not_null @unique "설비 태그"
- name: string(100) @not_null "설비명"
- status: AssetStatus = "planned" @group("상태") "상태"
- criticality: Priority = "normal" @group("상태") @display_labels(PriorityShortLabel) "중요도"
- installed_on: date? "설치일"
- decommissioned_at: datetime? "폐기시각"
- purchase_cost: decimal(14,2)? "취득원가"
- rated_power: float? "정격출력(kW)"
- efficiency: double? "효율"
- serial_no: string(60)? "제조번호"
- spec_sheet: text? "사양 요약"
- thumbnail: binary? "대표 이미지"

- floor_level: integer @lookup(floor_id.level) "층 번호"
- open_work_orders: integer @rollup(WorkOrder.asset_id, count) "작업지시 건수"
- total_labor_hours: decimal(12,2) @rollup(WorkOrder.asset_id, sum(labor_hours)) @indexed "총 작업시간"
- annual_depreciation: decimal(14,2) @computed(`purchase_cost / 10`) "연 감가상각"

- @index(floor_id, status)

## AssetSpec : Timestampable

> 공유 PK 1:1 확장 — PK 필드가 곧 부모(Asset) FK다.

- asset_id: identifier @pk @reference(Asset)
- manufacturer: string(80)? "제조사"
- model_name: string(80)? "모델명"
- weight_kg: decimal(10,3)? "중량"
- warranty_months: short? "보증개월"

## AssetDocument : Timestampable

- id: identifier @pk @generated
- asset_id: identifier @reference(Asset) @not_null
- kind: DocumentKind @not_null "문서 종류"
- title: string(120) @not_null "제목"
- byte_size: long? "파일 크기(bytes)"
- uploaded_at: timestamp = now() "업로드 시각"
- checksum: string(64)? "체크섬"

- @index(asset_id, kind)

## Vendor : Timestampable

- id: identifier @pk @generated
- code: string(20) @not_null @unique @index "유니크와 인덱스를 함께 선언 — 제약이 이미 인덱스를 소유한다"
- name: string(80) @not_null
- contract: ContractType = "spot" "계약 형태"
- contact_email: email? "담당자 메일"
- contact_phone: phone? "담당자 전화"

## Part : Timestampable

- id: identifier @pk @generated
- vendor_id: identifier? @reference(Vendor) "공급사"
- part_no: string(40) @not_null @unique "품번"
- name: string(100) @not_null "품명"
- category: PartCategory @not_null "분류"
- unit: MeasurementUnit = "each" "단위"
- unit_price: decimal(12,2)? "단가"
- currency: CurrencyCode = "krw" "통화"
- lead_time_days: short? "조달 리드타임"
- vendor_name: string @lookup(vendor_id.name) "공급사명"

## PartStock : Timestampable

> 재고 — 널 허용 컬럼을 포함한 복합 유니크. 필터드 유니크 인덱스가 나와야
> 두 번째 전체-NULL 행이 거부되지 않는다.

- id: identifier @pk @generated
- part_id: identifier @reference(Part) @not_null
- site_id: identifier @reference(Site) @not_null
- bin_code: string(20)? "보관 위치"
- lot_no: string(30)? "로트 번호"
- on_hand: integer = 0 "현재고"
- reserved: integer = 0 "예약수량"

- @unique(part_id, site_id, bin_code, lot_no)
- @index(site_id)

## WorkOrder : Timestampable

- id: identifier @pk @generated
- asset_id: identifier @reference(Asset) @not_null
- requester_id: identifier @reference(Employee) @not_null
- assignee_id: identifier? @reference(Employee) "담당자"
- order_no: string(30) @not_null @unique "지시번호"
- title: string(150) @not_null "제목"
- state: WorkOrderState = "draft" @group("진행") "상태"
- priority: Priority = "normal" @group("진행") "우선순위"
- kind: MaintenanceKind @not_null @group("진행") "정비 구분"
- requested_at: timestamp = now() "요청시각"
- due_at: datetime? "기한"
- closed_at: datetime? "종료시각"
- labor_hours: decimal(8,2) = 0 "작업시간"
- description: text? "상세"

- asset_tag: string @lookup(asset_id.tag) "설비 태그"
- asset_name: string @lookup(asset_id.name) "설비명"
- requester_name: string @lookup(requester_id.name) "요청자"
- task_count: integer @rollup(WorkOrderTask.work_order_id, count) "세부작업 수"
- part_cost: decimal(14,2) @rollup(WorkOrderPart.work_order_id, sum(line_total)) "부품비"
- total_cost: decimal(14,2) @computed(`part_cost + labor_hours * 30000`) "총원가"

- @index(asset_id, state)
- @index(assignee_id)

## WorkOrderTask : Timestampable

- id: identifier @pk @generated
- work_order_id: identifier @reference(WorkOrder) @not_null
- seq: integer @not_null "순번"
- name: string(120) @not_null "작업명"
- is_done: boolean = false "완료 여부"
- spent_hours: decimal(8,2)? "소요시간"

- @unique(work_order_id, seq)

## WorkOrderPart : Timestampable

- id: identifier @pk @generated
- work_order_id: identifier @reference(WorkOrder) @not_null
- part_id: identifier @reference(Part) @not_null
- quantity: decimal(10,3) @not_null "사용수량"
- unit_price: decimal(12,2) @not_null "적용단가"
- line_total: decimal(14,2) @not_null "금액"
- part_name: string @lookup(part_id.name) "품명"

- @index(work_order_id)

## MaintenancePlan : Timestampable

- id: identifier @pk @generated
- asset_id: identifier @reference(Asset) @not_null
- kind: MaintenanceKind @not_null "정비 구분"
- name: string(120) @not_null "계획명"
- interval_days: integer @not_null "주기(일)"
- is_enabled: boolean = true "사용 여부"
- schedule_count: integer @rollup(MaintenanceSchedule.plan_id, count) "예정 건수"

- @index(asset_id)

## MaintenanceSchedule : Timestampable

- id: identifier @pk @generated
- plan_id: identifier @reference(MaintenancePlan) @not_null
- planned_on: date @not_null "예정일"
- performed_on: date? "실시일"
- shift_id: identifier? @reference(ShiftPattern) "근무조"

- @unique(plan_id, planned_on)

## Inspection : Timestampable

- id: identifier @pk @generated
- asset_id: identifier @reference(Asset) @not_null
- inspector_id: identifier @reference(Employee) @not_null
- inspected_at: timestamp @not_null "점검시각"
- result: InspectionResult @not_null "판정"
- remark: text? "소견"
- item_count: integer @rollup(InspectionItem.inspection_id, count) "점검 항목 수"

- @index(asset_id, inspected_at)

## InspectionItem : Timestampable

- id: identifier @pk @generated
- inspection_id: identifier @reference(Inspection) @not_null
- name: string(120) @not_null "항목명"
- measured: double? "측정값"
- unit: MeasurementUnit = "each" "단위"
- lower_limit: double? "하한"
- upper_limit: double? "상한"
- result: InspectionResult @not_null "항목 판정"

- @index(inspection_id)

## Approval : Timestampable

- id: identifier @pk @generated
- work_order_id: identifier @reference(WorkOrder) @not_null
- approver_id: identifier @reference(Employee) @not_null
- decision: ApprovalDecision = "pending" "결재"
- decided_at: datetime? "결재시각"
- comment: text? "의견"

- @unique(work_order_id, approver_id)

## CostEntry : Timestampable

- id: identifier @pk @generated
- work_order_id: identifier @reference(WorkOrder) @not_null
- occurred_on: date @not_null "발생일"
- amount: decimal(16,4) @not_null "금액"
- currency: CurrencyCode = "krw" "통화"
- exchange_rate: decimal(12,6)? "환율"
- memo: string(200)? "적요"

- @index(work_order_id, occurred_on)

## Notification : Timestampable

- id: identifier @pk @generated
- recipient_id: identifier @reference(Employee) @not_null
- channel: NotificationChannel = "email" "발송 채널"
- severity: SeverityLevel = "info" "심각도"
- subject: string(150) @not_null "제목"
- body: text? "본문"
- sent_at: timestamp? "발송시각"
- read_at: timestamp? "확인시각"

- @index(recipient_id, sent_at)

## AuditEntry @internal

> 내부 감사 로그 — 데이터 API 표면에서 제외된다.

- id: identifier @pk @generated
- actor_id: identifier? @reference(Employee)
- entity_name: string(60) @not_null
- entity_id: identifier @not_null
- action: string(20) @not_null
- payload: json? "변경 내용"
- snapshot: binary? "원본 스냅샷"
- occurred_at: timestamp = now()
- created_at: timestamp = now()
- updated_at: timestamp = now()

## ServiceAccount @internal

> 전용 엔드포인트로 관리되는 인프라 엔티티.

- id: identifier @pk @generated
- name: string(60) @not_null @unique
- secret: string(120) @not_null
- rotated_at: timestamp? "교체시각"
- created_at: timestamp = now()
- updated_at: timestamp = now()
