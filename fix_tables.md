# Plands MVP Database Models (M3L) - Basic Format 수정본

> 한국 소셜 플랫폼 "Plands" MVP - Extended Format → Basic Format 변환
> **MVP 아키텍처**: Azure SQL Server + CosmosDB (하이브리드)
> **목표**: 모든 구성요소 포함, 기술적 복잡도만 단계별 발전, 8주 런칭

## User
> MVP 사용자 기본 정보 (Google OAuth 연동)

- Id: identifier @primary
- GoogleId: string(100) @unique
- Email: string(320) @unique
- Name: string(100)
- ProfileImageUrl?: string(500)
- Language: string(2) = "ko"
- IsActive: boolean = true
- CreatedAt: datetime = "@now"
- UpdatedAt?: datetime

## Location
> MVP: 장소 정보 (정규화된 별도 엔티티)

- Id: identifier @primary
- LocationName: string(300) "사용자 지정 장소명 (중요: 사용자가 입력한 장소 이름)"
- Address: string(500)? "지오코딩 주소 (지도에서 가져온 실제 주소)"
- Latitude: decimal "위치 좌표 (위도) - 필수값 (지도 기반 입력 제약)"
- Longitude: decimal "위치 좌표 (경도) - 필수값 (지도 기반 입력 제약)"
- Country: string(100)? "국가 정보"
- Region: string(100)? "지역 정보 (시/도)"
- Category: string(50)? "장소 카테고리"
- UsageCount: integer = 0 "사용 횟수 (추천 우선순위용)"
- CreatedBy: identifier @reference(User)! "장소 등록 사용자 (감사 추적)"
- LastUsedAt: datetime? = "@now" "마지막 사용일 (추천 우선순위용)"
- CreatedAt: datetime = "@now"
- UpdatedAt?: datetime

- @index(LocationName) # 부분일치 검색용
- @index(Latitude, Longitude) # 주변 장소 검색용
- @index(UsageCount, LastUsedAt) # 추천 우선순위용
- @index(CreatedBy, CreatedAt)

## Plan
> MVP 핵심: 사용자가 만드는 계획/모임 (단순화)

- Id: identifier @primary
- Title: string(200)
- Description?: string(1000)
- ImageUrl?: string(500)
- StartDateTime: datetime
- EndDateTime?: datetime
- LocationId: identifier? @reference(Location)? "선택적 장소 참조"
- CreatedBy: identifier @reference(User) "계획 생성자"
- IsPublic: boolean = true
- MaxParticipants?: integer
- PlanType: PlanType = "OneTime"
- SeriesId?: identifier @reference(Series)?
- Tags?: string(200)
- CreatedAt: datetime = "@now"
- UpdatedAt?: datetime

- @index(CreatedBy, CreatedAt)
- @index(StartDateTime, IsPublic)
- @index(LocationId, StartDateTime)
- @index(SeriesId)

## PlanParticipation
> MVP: 계획 참여자 관리

- Id: identifier @primary
- PlanId: identifier @reference(Plan)
- UserId: identifier @reference(User)
- Status: ParticipationStatus = "Pending"
- JoinedAt?: datetime
- CreatedBy: identifier @reference(User)! "참여 요청자 (감사 추적)"
- InvitedBy?: identifier @reference(User)! "초대자 (감사 추적)"
- CreatedAt: datetime = "@now"
- UpdatedAt?: datetime

- @unique(PlanId, UserId)
- @index(UserId, Status)
- @index(PlanId, Status)

## Follow
> MVP: 사용자 팔로우 시스템

- Id: identifier @primary
- FollowerId: identifier @reference(User) "팔로우하는 사용자"
- FollowingId: identifier @reference(User)! "팔로우 받는 사용자 (CASCADE 충돌 방지)"
- CreatedAt: datetime = "@now"

- @unique(FollowerId, FollowingId)
- @index(FollowerId)
- @index(FollowingId)

## PlanInvitation
> MVP: 계획 초대 시스템

- Id: identifier @primary
- PlanId: identifier @reference(Plan)
- InviterId: identifier @reference(User) "초대하는 사용자"
- InviteeId: identifier @reference(User)! "초대받는 사용자 (CASCADE 충돌 방지)"
- Status: InvitationStatus = "Pending"
- CreatedBy: identifier @reference(User)! "생성자 (감사 추적)"
- CreatedAt: datetime = "@now"
- UpdatedAt?: datetime

- @unique(PlanId, InviteeId)
- @index(InviteeId, Status)
- @index(PlanId, Status)

## Series
> MVP: 정기 모임 시리즈

- Id: identifier @primary
- Title: string(200)
- Description?: string(1000)
- CreatedBy: identifier @reference(User)
- IsActive: boolean = true
- RecurrencePattern?: string(100)
- DefaultLocationId?: identifier @reference(Location)?
- ImageUrl?: string(500)
- Tags?: string(200)
- MembershipType: MembershipType = "Open"
- MaxMembers?: integer
- CreatedAt: datetime = "@now"
- UpdatedAt?: datetime

## SeriesFollower
> MVP: 시리즈 팔로워

- Id: identifier @primary
- SeriesId: identifier @reference(Series)
- UserId: identifier @reference(User)
- CreatedAt: datetime = "@now"

- @unique(SeriesId, UserId)

## PostScript
> MVP: 계획 후기 시스템

- Id: identifier @primary
- PlanId: identifier @reference(Plan)
- UserId: identifier @reference(User)
- Content: string(1000)
- ImageUrls?: string(2000)
- Rating?: integer
- IsPublic: boolean = true
- CreatedAt: datetime = "@now"
- UpdatedAt?: datetime

## ChatMessage
> MVP: 계획 채팅 메시지

- Id: identifier @primary
- PlanId: identifier @reference(Plan)
- SenderId: identifier @reference(User) "메시지 발신자"
- Content: string(500)
- MessageType: MessageType = "Text"
- CreatedBy: identifier @reference(User)! "생성자 (감사 추적)"
- CreatedAt: datetime = "@now"

## Notification
> MVP: 알림 시스템

- Id: identifier @primary
- UserId: identifier @reference(User)
- Type: NotificationType
- Title: string(100)
- Content: string(500)
- RelatedEntityId?: string(50)
- IsRead: boolean = false
- CreatedAt: datetime = "@now"

## PlanLike
> MVP: 계획 좋아요

- Id: identifier @primary
- PlanId: identifier @reference(Plan)
- UserId: identifier @reference(User)
- CreatedAt: datetime = "@now"

- @unique(PlanId, UserId)

## Bookmark
> MVP: 북마크

- Id: identifier @primary
- UserId: identifier @reference(User)
- PlanId: identifier @reference(Plan)
- CreatedAt: datetime = "@now"

- @unique(UserId, PlanId)

## UserSettings
> MVP: 사용자 설정

- Id: identifier @primary
- UserId: identifier @reference(User) @unique
- NotificationEnabled: boolean = true
- EmailNotificationEnabled: boolean = true
- PrivacyLevel: PrivacyLevel = "Public"
- Language: string(2) = "ko"
- Timezone: string(50) = "Asia/Seoul"
- CreatedAt: datetime = "@now"
- UpdatedAt?: datetime

## RefreshToken
> MVP: 인증 토큰 관리

- Id: identifier @primary
- UserId: identifier @reference(User)
- Token: string(500) @unique
- ExpiresAt: datetime
- IsRevoked: boolean = false
- CreatedAt: datetime = "@now"

## PlanType ::enum
- OneTime: "일회성 계획"
- Recurring: "정기 계획"

## ParticipationStatus ::enum
- Pending: "참여 대기"
- Confirmed: "참여 확정"
- Declined: "참여 거절"
- Cancelled: "참여 취소"

## InvitationStatus ::enum
- Pending: "초대 대기"
- Accepted: "초대 수락"
- Declined: "초대 거절"
- Expired: "초대 만료"

## MembershipType ::enum
- Open: "공개"
- InviteOnly: "초대 전용"
- Closed: "비공개"

## MessageType ::enum
- Text: "텍스트"
- Image: "이미지"
- System: "시스템 메시지"

## NotificationType ::enum
- PlanInvitation: "계획 초대"
- PlanUpdate: "계획 업데이트"
- NewFollower: "새 팔로워"
- PlanReminder: "계획 알림"

## PrivacyLevel ::enum
- Public: "공개"
- FriendsOnly: "친구 공개"
- Private: "비공개"