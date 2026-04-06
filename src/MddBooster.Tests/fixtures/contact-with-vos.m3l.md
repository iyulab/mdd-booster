# Namespace: test.contact

## Contact

> VO 매핑 테스트 픽스처

- id: identifier @pk @generated
- name: string(50) @not_null "이름"
- phone_number: phone @not_null "전화"
- email_address: email "이메일"
- homepage: url? "홈페이지"
