# ked-motion-tool

캐릭터 모션 전용 저작 도구. 무대 런타임(`ked-stage-runtime`)이 읽는 `motions.json`(`motions/1`)을
마야급 커브 편집으로 만든다. 계획 전체는 [`docs/PLAN.md`](docs/PLAN.md).

- **v1**: 초상화 한 장을 리그 축(`Track_Move_*`·`SwayPivot`…)으로 움직이는 단순 캐릭터 모션. 그래프
  편집기(키·탄젠트)·뷰포트·도프 시트.
- **v2 이후**: 카메라·depth·intent 채널, Spine 식 다중 스프라이트 리그, 컷인·배경 연출.
- 툴은 Yarn·커맨드를 **모른다**. 산출물은 `motions.json` 하나다.

## 처음 써 보기 (10분)

리그는 툴에서 **만들지 않는다**. 유니티가 이미 내보낸 덤프 `ExportedTuning/rig-schemas.json`(ked-stage-runtime·ked-presentation-runtime 둘 다 같은 파일)을 읽는다 — 노드 이름·계층·앵커·피벗이 전부 거기서 온다.

1. **실행** — 저장소 루트에서:
   ```
   dotnet run --project src/Mt.App/Mt.App.csproj
   ```
2. **리그 덤프…** → `ked-stage-runtime/ExportedTuning/rig-schemas.json`. 옆의 `base-resolution.json`도 같이 실린다. 상태 줄에 `리그 덤프: character, background, screenEffect · 무대 1920×1080`.
3. **초상 PNG…** → `ked-presentation-runtime/Assets/Samples/Portraits/harin/b/01.png`. 뷰포트에 초상이 선다(없으면 자리표시 사각형).
4. **motions.json 가져오기…** → `ked-presentation-runtime/Assets/Samples/Dialogue/motions.json`. 왼쪽 "모션"에 `nod`·`bow`가 생긴다.
5. `nod`를 고르고 뷰포트를 한 번 클릭한 뒤 **Space** — 끄덕인다. `bow`는 끝에서 8도 기운 채 남는다(commit).
6. 그래프 탭에서 키를 끌어 본다 — 놓는 순간 뷰포트가 따라오고, Ctrl+Z로 돌아온다. 도프 시트 탭에서 블록을 끌면 시간이 옮겨진다.
7. **다른 이름으로…** → `*.motion-authoring.json`으로 저장(저작 원본). **motions.json 내보내기…** → 런타임이 읽는 산출물. 아래 문제 칸이 비어 있을 때만 나간다.

**처음부터 만들기**: 왼쪽 폼에 이름 `wave`, 리그 `character`, 길이 `0.5`, 끝 `restore` → **모션 추가**. 노드 `CharacterPortrait_Track_Move_X` · `position` → **채널 추가**. 채널 행의 **x** 토글을 켜면 현재 시각에 중립값 키 하나로 성분이 생긴다. 보기의 현재 시각을 `0.25`로 두고 **키 끼우기(I)**, 인스펙터 값에 `30` Enter. 현재 시각 `0.5`에서 다시 **I**, 값 `0`. 전체 선택 후 **flat(2)**. 문제 칸이 비면 내보낸다.

## 구조

```
Ked.Presentation.Core (복사 반입)  ──►  Mt.Authoring  ──►  Mt.App (Avalonia)
tests: Ked.Presentation.Core.Tests(NUnit, 원본 그대로) · Mt.Authoring.Tests(xunit) · Mt.App.Tests(Avalonia.Headless)
```

| 프로젝트 | 역할 |
|---|---|
| `src/Ked.Presentation.Core` | 무대 런타임의 연출 코어 복사본. 모션 규약·수학·검증·리그 수학의 유일한 출처. 출처와 동기화 절차는 [`vendoring.md`](src/Ked.Presentation.Core/vendoring.md) |
| `src/Mt.Authoring` | 저작 도메인. Avalonia를 모른다. `Model/`(저작 원본 `*.motion-authoring.json`) · `Editing/MotionEditor`(모든 편집의 통로, 스냅샷 undo) · `Serialization/`(`MotionDocumentJson` 저작 원본, `MotionsJson` 산출물 `motions/1` 정규 쓰기·관용 읽기) · `Validation/`(코어 `MotionRules`로 편집 중 검증, 0건일 때만 내보내기) · `Rig/RigSchemaFile`(리그 덤프 + SHA-256) |
| `src/Mt.App` | 그리기와 입력. `Views/ViewportView`(무대 — 포즈는 `Mt.Authoring/Preview`의 코어 수학, 재생 시계·스크럽·어니언 스킨) · `Views/DopeSheetView`(도프 시트) · `Views/GraphEditorView`(그래프 에디터 — 드래그 중간값은 렌더만, 놓는 순간이 편집 하나) · `MainWindow`(셸: 모션·채널 목록, 인스펙터, 파일) · `Services/DocumentSession`(경로·리그 덤프·저장·내보내기). 좌표 변환·히트 테스트·탄젠트 산수는 `Mt.Authoring/Graph`의 순수 함수다 |

## 그래프 에디터 조작

| 조작 | 뜻 |
|---|---|
| 클릭 · Shift+클릭 · 빈 자리 드래그 · Ctrl+드래그 | 키 선택 · 추가 선택 · 박스 선택 · 라소 |
| **MMB 드래그** (어느 툴이든) · 이동 툴(**W**)의 LMB 드래그 · Shift = 축 잠금 | 선택 키 시간·값 이동(마야). 선택 툴(**Q**)의 LMB는 고르기만. 시간은 1/60초 스냅(끌 수 있다) |
| 스케일 툴(**R**) LMB 드래그 · Shift = 한 축만 | 선택 중심(피벗) 기준 값·시간 스케일 — 오른쪽 = 시간, 위 = 값 |
| **Ctrl+C / X / V** (그래프·도프 시트 공용) | 복사 · 잘라내기 · 현재 시각에 붙여넣기(가장 이른 키를 현재 시각에, 같은 시각은 덮어씀, 없는 채널은 만든다) |
| 버퍼 커브 **Ctrl+B** · **Ctrl+Shift+B** | 보이는 커브를 흐린 점선으로 찍어 두기 · 찍어 둔 것과 지금을 바꾸기(마야 Buffer Curve) |
| 보기 › 사이클 미리보기 | 길이로 감아 반복한 곡선을 양옆에 흐리게(보기만 — 실제 끝은 restore/commit) |
| 인스펙터 시각·값 칸 `+=3` `-=1` `*=2` `/=2` | 선택 키 전부에 상대 적용(마야 채널 박스). 숫자는 절대값 |
| 더블클릭 · **I** | 커브 위 그 시각에 키 · 현재 시각에 키(값은 곡선 위) |
| **Delete** · **Ctrl+Z / Y** | 키 삭제 · 되돌리기/다시하기 |
| 핸들 드래그 · **Ctrl+1 … Ctrl+6** | 기울기(끊긴 키는 한쪽만) · auto / flat / linear / free / spline / plateau. 새 키의 탄젠트는 "보기 › 새 키 탄젠트"에서 (가중·계단 탄젠트는 규약이 거부한다) |
| 휠 (Ctrl = 시간만 · Shift = 값만) · **Alt+MMB** · **Alt+RMB 드래그** · **A** · **F** | 줌 · 팬 · 줌 드래그(오른쪽 = 시간, 위 = 값) · 전체 맞춤 · 선택 맞춤 |
| 채널 행의 x · y · z · 🔓/🔒 · 🔊/🔇 | 보이기(없는 성분은 켜는 순간 현재 시각의 중립값 키 하나로 생긴다) · 잠금(보이되 못 만짐) · 뮤트(뷰포트 재생에서 뺌, 저장 안 됨) |
| 보기 › 그래프 보기: 절대 / 정규화 / 쌓기 | 마야 Normalize·Stacked — 커브마다 제 범위를 0~1로 눌러 모양만 견준다. 끌면 값은 실제 단위로 바뀐다 |

## 전역 단축키 (마야 타임 슬라이더)

창 어디에 포커스가 있든 듣는다(글 입력 칸만 예외). 오른쪽 "키 › 현재 시각" 줄에 같은 버튼이 있다.

| 키 | 뜻 (마야) |
|---|---|
| **1** · **2** | 이전 키 · 다음 키 |
| **Alt+1** · **Alt+2** | 이전 프레임 · 다음 프레임 (24fps, ←/→와 같다) |
| **Alt+Q** | 재생 토글 (PlaybackToggle) |
| **Shift+W** | 현재 시각의 키 전부(모든 커브) 복사 (timeSliderCopyKey) |
| **Shift+E** | 현재 시각에 붙여넣기 — 같은 시각은 덮어쓰고 없는 채널은 만든다 (timeSliderPasteKey) |
| **Shift+F** | 현재 시각의 키 전부 지우기 (timeSliderClearKey) |
| **Shift+1** · **Shift+2** | 인비트윈 빼기 · 넣기 — 현재 시각 **뒤**의 키를 한 프레임 당기거나 민다. 그 시각의 키는 안 움직이고, 당겨서 닿으면 움직인 키가 이긴다 (timeSliderEditKeys removeInbetween / addInbetween) |

## 도프 시트

아래 영역의 "도프 시트" 탭. 그래프와 **선택이 하나**다(`KeySelection` 한 객체를 두 뷰가 든다) — 여기서 고른 키가 그래프에서도 선택돼 있다.

| 조작 | 뜻 |
|---|---|
| 블록 클릭 · Shift · 박스 | 선택. 요약 행(노드·채널)의 블록 하나는 그 밑의 키 전부 |
| 블록 드래그 · Alt+드래그 · **Ctrl+D** | 시간 이동 · 복제 · 한 프레임 뒤에 복제 |
| 눈금자 아래 범위 띠의 양끝 핸들 | 시간 스케일(반대쪽 끝이 기준점) |
| 성분 행 더블클릭 · **Delete** | 그 시각에 키(값은 곡선 위) · 삭제 |
| 눈금자 클릭·드래그 · 노드 행 클릭 | 스크럽 · 접기/펼치기 |
| 휠 · 가운데 버튼 드래그 · **A** | 시간 줌 · 팬 · 전체 맞춤 |

움직인 키가 안 움직인 키의 시각에 닿으면 움직인 키가 이긴다(마야와 같다). 시간은 0 아래로 내려가지 않는다.

## 뷰포트

- **매니퓰레이터**: 오른쪽 "뷰포트 조작"에서 노드를 고르고 **W** 이동 · **E** 회전 · **R** 배율 · **Q** 선택. 기즈모의 축 끝은 한 축만, 가운데는 자유. 놓는 순간 **Auto Key**면 키가 들어가고(바뀐 성분만), 아니면 "키 없는 포즈"로 남아 **S**로 키를 놓거나 **Esc**/시각 이동으로 버린다. 그 시각에 이미 키가 있으면 바로 덮어쓴다. 채널·성분이 없으면 모션 기본 mode로 만든다.
- **타임 슬라이더**(아래 띠): 키 틱(선택 키는 노랑) · 클릭/드래그 스크럽 · 양끝 핸들 = 재생 범위 · `.` `,` 다음/이전 키 · 한 번/루프/왕복 · 속도.
- 리그 덤프(`rig-schemas.json`)로 세운 트리 위에 초상 PNG 한 장. 사이징은 런타임 `HeightFitPreserveAspect`와 같다. 깊이 mid, 카메라 없음(v1).
- 포즈 = 코어 `MotionMath.Compose`. 길이를 지나면 restore는 시작 상태, commit은 마지막 키 — 재생도 그 규칙을 보여 준다.
- **Space** 재생/일시정지 · **Home/End** 처음/끝 · **←/→** 한 프레임(24fps) · 아래 띠 드래그 = 스크럽. 그래프의 현재 시각과 하나다.
- 어니언 스킨: 앞뒤 2프레임 반투명. 선택한 키의 노드에 피벗·x(빨강)·y(초록) 축.
- `base-resolution.json`은 리그 덤프 옆에서 자동으로 싣는다(없으면 1920×1080).

## 설정 · 저장

- 설정은 `%APPDATA%/ked-motion-tool/settings.json`(스냅·스냅 간격·새 키 탄젠트·어니언 프레임·Auto Key·자동 저장 간격·최근 파일 8개). 창을 닫을 때 저장.
- 자동 저장은 문서 옆 `<이름>.autosave.motion-authoring.json`(기본 5분, 바뀐 것이 있을 때만). 저장 상태는 건드리지 않는다.
- 아래 문제 칸의 각 줄에 **돕는 손** 버튼: 끝 키를 길이로 · 길이를 마지막 키에 · 첫/마지막 키를 중립값으로 · 첫 키를 0초로 · 채널을 relative로 · 끝을 commit으로. 각각 되돌리기 한 번.
- relative 채널의 전 구간 중립값 성분은 그래프에서 흐리게 보이고 내보내기에서 빠진다(Unity 내보내기와 같은 규칙).

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
| M1 데이터 — 저작 원본 모델 · `MotionEditor`(undo) · 가져오기/내보내기 · 정규 직렬화 · 검증 · 리그 덤프 | 완료 — `nod`·`bow` 바이트 동일 테스트 |
| M2 그래프 편집기 — 초 단위·팬/줌·키 선택/박스/드래그/끼우기/삭제·탄젠트 모드와 핸들·스냅·셸(모션·채널·인스펙터·파일) | 완료 — 빈 문서에서 `nod`를 처음부터 만들어 내보낸 바이트가 Unity 산출물과 같은 테스트 |
| M3 뷰포트 — 리그 덤프 트리 · 기준 해상도 무대 · 초상 PNG(런타임 사이징) · 재생/루프/스크럽 · 어니언 스킨 · 노드 축 · restore/commit 끝 규칙 | 완료 |
| M4 도프 시트 — 노드›채널›성분 트리 · 요약/세부 블록 · 이동/복제/시간 스케일 · 그래프와 같은 선택 | 완료 |
| M5 UX — 타임 슬라이더 · 매니퓰레이터/Auto Key · spline/plateau · 마야식 그래프 조작 · 정규화/잠금/뮤트/복사/버퍼/사이클 · 돕는 손/설정/자동 저장 | 완료 — [`docs/PLAN-M5.md`](docs/PLAN-M5.md), 외형 손질은 사용 피드백으로 |
| M6 통합 | 다음 — [`docs/PLAN.md`](docs/PLAN.md) |
