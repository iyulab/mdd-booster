# mdd-booster에 기여하기

관심 가져줘서 고맙다. mdd-booster는 하나의 M3L AST를 여러 타깃(Sql·Model·Api·TypeScript)으로
독립 방출하는 생성기다 — 대부분의 기여는 특정 타깃 생성기 하나를 건드리게 되므로, 그 변경이
다른 타깃의 계약을 조용히 깨지 않는지가 언제나 리뷰의 핵심이다.

## 기본 규칙

- **[`docs/design-principles.md`](./docs/design-principles.md) 를 먼저 읽는다.** 고정된 것을
  정의한다. 앵커와 충돌하는 제안은 우회하지 말고 앵커를 바꾸자는 논의로 낸다.
- **이 리포는 M3L을 NuGet 패키지(`M3L.Native`)로만 소비한다.** `m3l` 소스를 로컬에서 고쳐도
  pack·게시·버전 핀 상승 없이는 이 리포에 아무 영향이 없다 — `ProjectReference` 로 우회하지
  않는다(설계 앵커 「지식은 아래로만」 참조).
- **생성 코드는 자신을 실행할 런타임을 모른다.** `Iyu.*` 네임스페이스를 아는 코드(예:
  `using Iyu.Core.Entities;` 방출)를 추가하지 않는다 — 소비앱의 `GlobalUsings.cs` 가 그 자리다.
  이 규칙을 어긴 전례(정규화 경로 `global::Iyu.*` 방출)의 회귀 가드가
  `EntityPairRendererTests`·`BuildCommandFullFixtureTests` 에 있다 — 새 코드가 이 가드를 우회하는
  형태(문자열 결합으로 같은 결과를 만드는 등)로 통과하게 하지 않는다.
- **타깃 하나를 건드리면 축 비대칭을 확인한다.** `MddBooster.Tests` 의
  `ModelTargetAxisCoverageTests`(`AsymmetricGap` 항목)가 "다른 타깃은 처리하는데 이 타깃만
  빠뜨린 선언"을 잡는 래칫이다 — 새 속성/제약을 한 타깃에만 반영하고 끝내지 않는다. 의도적으로
  비대칭이면(예: 아직 어느 타깃도 처리하지 않는 새 속성) 그 사실 자체를 PR 설명에 적는다.

## 개발

.NET 10 SDK 가 필요하다.

```bash
dotnet build MddBooster.slnx
dotnet test                                          # MddBooster.Tests
dotnet test --filter "FullyQualifiedName~<Name>"     # 테스트 하나만
dotnet run --project src/MddBooster.Cli -- build <mdd.json 이 있는 디렉터리>
dotnet run --project src/MddBooster.Cli -- build ./samples   # 바로 돌려볼 수 있는 예시
```

`TreatWarningsAsErrors` 가 켜져 있다 — 경고가 빌드를 깬다.

### Acceptance 게이트

`src/MddBooster.Tests/fixtures/large-model-acceptance.m3l.md` 는 **설계된** 픽스처다 — 멀티라인
description 의 `///` 연속, 널 허용 복합 유니크, 널 허용 64-bit 세 형태가 의도적으로 심겨 있다.
정리하다 지우지 않는다(이유는 픽스처 상단 주석 참조). `MDDBOOSTER_ACCEPTANCE_MODEL` 환경변수로
다른 모델을 가리켜 같은 게이트를 그 모델로 돌릴 수 있다 — 경로가 잘못되면 건너뛰지 않고
**실패**한다.

### 레이아웃

```
src/
  MddBooster.Core/              # AST 소비(M3L.Native) · 시맨틱 분석 · 타깃 공통 모델
  MddBooster.Generators.Sql/    # SSDT 스키마 + Schemorph 대상 SQL 방출
  MddBooster.Generators.Model/  # EF Core 엔티티 + DbContext 방출
  MddBooster.Generators.Api/    # OData/GraphQL 엔티티 페어 등록 코드 방출
  MddBooster.Generators.TypeScript/  # 타입·폼·enum 방출
  MddBooster.Cli/                # `mdd build` 등 CLI 진입점
  MddBooster.Tests/              # 단위 + acceptance + fixtures/
```

각 `Generators.*` 는 서로를 참조하지 않는다 — 공유하는 것은 `MddBooster.Core` 가 만든 AST와
시맨틱 모델뿐이다. 한 타깃의 출력 형태를 바꾸려고 다른 타깃 프로젝트를 참조하게 만드는 변경은
이 경계를 깬다.

## 품행

친절하게, 직접적으로, 선의를 전제한다. 이견은 설계 원칙에 대한 논거의 질로 가른다.

## 라이선스

기여는 이 프로젝트의 MIT 라이선스로 수용된다.
