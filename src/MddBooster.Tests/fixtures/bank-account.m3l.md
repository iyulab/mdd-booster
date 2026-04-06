# Namespace: test.bankaccount

## Timestampable ::interface

- created_at: timestamp = now()
- updated_at: timestamp = now()

---

## BankAccount : Timestampable

> 법인 계좌 (테스트 픽스처)

- id: identifier @pk @generated
- bank_name: string(30) @not_null "은행명"
- account_number: string(40) @not_null @unique "계좌번호"
- holder_name: string(30) @not_null "예금주"
- note: string(200)? "비고"
- is_active: boolean = true
