# ked-motion-tool

캐릭터 모션 전용 저작 도구. 무대 런타임(`ked-stage-runtime`)이 읽는 `motions.json`(`motions/1`)을
마야급 커브 편집으로 만든다. 계획 전체는 [`docs/PLAN.md`](docs/PLAN.md).

- **v1**: 초상화 한 장을 리그 축(`Track_Move_*`·`SwayPivot`…)으로 움직이는 단순 캐릭터 모션. 그래프
  편집기(키·탄젠트)·뷰포트·도프 시트.
- **v2 이후**: 카메라·depth·intent 채널, Spine 식 다중 스프라이트 리그, 컷인·배경 연출.
- 툴은 Yarn·커맨드를 **모른다**. 산출물은 `motions.json` 하나다.

## 구조

```
Ked.Presentation.Core (복사 반입)  ──►  Mt.Authoring  ──►  Mt.App (Avalonia)
tests: Ked.Presentation.Core.Tests(NUnit, 원본 그대로) · Mt.Authoring.Tests(xunit) · Mt.App.Tests(Avalonia.Headless)
```

| 프로젝트 | 역할 |
|---|---|
| `src/Ked.Presentation.Core` | 무대 런타임의 연출 코어 복사본. 모션 규약·수학·검증·리그 수학의 유일한 출처. 출처와 동기화 절차는 [`vendoring.md`](src/Ked.Presentation.Core/vendoring.md) |
| `src/Mt.Authoring` | 저작 도메인. 문서(`*.motion-authoring.json`)·편집 명령·되돌리기·내보내기. Avalonia를 모른다 |
| `src/Mt.App` | 그리기와 입력. 좌표 변환·히트 테스트는 순수 함수로 두고 테스트한다 |

## 빌드·테스트

```
dotnet restore KedMotionTool.sln
dotnet build KedMotionTool.sln --configuration Release
dotnet test  KedMotionTool.sln --configuration Release
dotnet run --project src/Mt.App/Mt.App.csproj
```

.NET 10 SDK · Avalonia 12.1.1. 판은 `Directory.Packages.props`에서 한 곳으로 관리한다 (VnTool과 같은 판).

## 규약 출처

- 산출물 규약: `ked-stage-runtime/docs/motion-contract.md` (`motions/1`, 정규 직렬화 §7)
- 리그 노드: `rig-schemas.json` (무대 런타임의 `Ked.Presentation.Core.RigSchemaLoader`로 읽는다)
- 산출물 등가의 심판: 무대 런타임의 유니티 `MotionClipExporter` — 같은 입력이면 바이트까지 같아야 한다

## 진행

| 단계 | 상태 |
|---|---|
| M0 뼈대 — 솔루션 · Core 반입 · 빈 창 · 헤드리스 테스트 · CI | 완료 |
| M1 데이터 — 저작 원본 ↔ `motions.json` 왕복 | 다음 |
| M2 그래프 편집기 · M3 뷰포트 · M4 도프 시트 · M5 UX · M6 통합 | [`docs/PLAN.md`](docs/PLAN.md) |
