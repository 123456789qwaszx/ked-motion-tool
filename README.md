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
5. `nod`를 고르고 **Alt+`** — 끄덕인다. `bow`는 끝에서 8도 기운 채 남는다(commit).
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

## 그래프 에디터 (마야 그래프 에디터 그대로)

아래 "그래프" 탭. 왼쪽 **아웃라이너** · 위 **툴바** · 회색 플롯(재생 범위 안은 밝게, 위 눈금자는 **프레임**, 값은 왼쪽, 현재 시각은 노란 선과 마커). 시간 표시는 설정에서 초로 바꿀 수 있다(칸에는 `12`·`12f`·`0.5s`처럼 단위를 붙여 쓸 수 있다).

| 조작 | 뜻 |
|---|---|
| 아웃라이너 행 클릭 · Shift/Ctrl+클릭 · 검색 칸 | 그 커브만 보이기 · 더하기/빼기 · 노드·종류로 거르기. 없는 성분(흐림)은 클릭하면 현재 시각의 중립값 키 하나로 생긴다. 채널 행의 🔓/🔒 · 🔊/🔇 · rel/abs · × |
| 클릭 · Shift+클릭 · 빈 자리 드래그 · Ctrl+드래그 | 키 선택 · 추가 선택 · 박스 선택 · 라소 |
| **커브 선 클릭** · Shift+클릭 · **박스 드래그가 커브를 지나면** | 커브 선택(흰색 굵게) · 더하기/빼기 · 정확히 안 눌러도 상자가 지나는 커브가 선택된다. 빈 자리 클릭은 푼다 |
| 커브를 고른 채 **MMB 클릭** | 그 커브 위, 그 시각(스냅)에 키(값은 곡선 위) — 마야 Insert Key |
| 선택 키 둘레에서 **RMB 드래그** · **Shift+RMB 드래그** · 빈 자리 **RMB 드래그** | 처음 움직인 쪽으로만(가로 = 시각, 세로 = 값) 한 번에 이동 · Shift면 정수 프레임(1fr, 2fr…)·정수 값에 스냅 · 화면 이동(팬) |
| **MMB 드래그** (어느 툴이든) · 이동 툴(**W**)의 LMB 드래그 · Shift = 축 잠금 | 선택 키 시간·값 이동(마야). 선택 툴(**Q**)의 LMB는 고르기만. 시간은 스냅, 값은 **값 스냅**을 켜면 정수 |
| 스케일 툴(**R**) LMB 드래그 · Shift = 한 축만 | 선택 중심(피벗) 기준 값·시간 스케일 — 오른쪽 = 시간, 위 = 값 |
| **리전 툴** | 상자로 고르면 상자가 남는다. 상자 안 드래그 = 이동, 왼/오른 모서리 = 반대쪽 끝 기준 시간 스케일, 위/아래 모서리 = 값 스케일 |
| 위 눈금자 클릭·드래그 | 스크럽 |
| 툴바 끼우기(**I**) · 전체(**A**) · 선택(**F**) · 재생 범위 · 현재 시각 가운데 | 현재 시각에 키(값은 곡선 위) · 맞춤 넷(마야 Frame All / Frame Selected / Frame Playback Range / Center Current Time) |
| 툴바 auto · spline · linear · flat · plateau · free · 끊기 · 붙이기 · **Ctrl+1 … Ctrl+6** | 선택 키 탄젠트. 핸들(흰 선, 네모 끝) 드래그 = 기울기(끊긴 키는 한쪽만). 새 키의 탄젠트는 "보기 › 새 키 탄젠트" (가중·계단 탄젠트는 규약이 거부한다) |
| 툴바 버퍼 찍기 · 바꾸기 · 지우기 · **Ctrl+B** · **Ctrl+Shift+B** | 보이는 커브를 흐린 점선으로 찍어 두기 · 찍어 둔 것과 지금을 바꾸기(마야 Buffer Curve) |
| 툴바 무한(앞 · 뒤): 상수 / 사이클 / 사이클+오프셋 / 왕복 / 직선 | 마야 Pre/Post Infinity — 첫/마지막 키 밖을 흐린 점선으로 이어 보인다(보기만 — 산출물의 끝은 restore/commit) |
| 툴바 Stats 시각 · 값 칸 `+=3` `-=1` `*=2` `/=2` | 선택 키 전부에 상대 적용(마야 채널 박스). 숫자는 절대값. 시각은 표시 단위(프레임) |
| **Ctrl+C / X / V** (그래프·도프 시트 공용) | 복사 · 잘라내기 · 현재 시각에 붙여넣기(가장 이른 키를 현재 시각에, 같은 시각은 덮어씀, 없는 채널은 만든다) |
| 더블클릭 · **Delete** · **Ctrl+Z / Y** | 커브 위 그 시각에 키 · 키 삭제 · 되돌리기/다시하기 |
| 휠 (Ctrl = 시간만 · Shift = 값만) · **Alt+MMB** · **Alt+RMB 드래그** | 줌 · 팬 · 줌 드래그(오른쪽 = 시간, 위 = 값) |
| 툴바 보기: 절대 / 정규화 / 쌓기 | 마야 Normalize·Stacked — 커브마다 제 범위를 0~1로 눌러 모양만 견준다. 끌면 값은 실제 단위로 바뀐다 |

## 타임 슬라이더 · 레인지 슬라이더 · 트랜스포트 (마야 그대로)

뷰포트 아래 두 줄. 위 줄은 **타임 슬라이더**(재생 범위를 프레임 눈금으로, 빨간 키 틱, 현재 프레임은 검은 상자) + `[현재 프레임] ⏮ |◀ ◁ ◀ ▶ ▷ ▶| ⏭`. 아래 줄은 `[0][범위 시작] ═레인지 바═ [범위 끝][길이]` + 재생 모드 · 속도 · 24 fps · **Auto Key** · 어니언 · 초상.

| 조작 | 뜻 |
|---|---|
| LMB 클릭·드래그 | 스크럽(프레임 스냅). 빨간 범위 선택은 지워진다 |
| **Shift+LMB 드래그** | 범위 선택(빨강, 프레임 단위). 한 프레임만 고르면 그 프레임의 키가 선택된다 |
| 범위 안 **MMB 드래그** · 범위 양끝 LMB 드래그 | 범위의 키 이동 · 반대쪽 끝 기준 시간 스케일(놓을 때 편집 하나, 끄는 동안 노란 유령 틱) |
| 범위 밖 **MMB 드래그** | **포즈 들고 스크럽** — 보이는 포즈는 그대로 두고 시각만 간다(현재 프레임 상자에 점선 테두리). **S**로 그 포즈를 그 프레임에 키(모든 채널, 되돌리기 한 번) · Esc/보통 스크럽은 버린다 |
| RMB 메뉴 | 키 잘라내기/복사/붙여넣기/삭제(범위, 없으면 현재 프레임) · 키를 프레임에 스냅 · 재생 범위를 선택으로/전체로 · 범위 선택 지우기 · 재생 모드 · 재생 속도 · 키 틱 표시(전체/선택/없음) |
| 레인지 바 끌기 · 양끝 · 더블클릭 | 재생 범위를 통째로 옮기기 · 크기 · 전체 ↔ 직전 범위 |
| 길이 칸 | 모션 길이(애니메이션 끝). 바꾸면 길이가 바뀐다 |
| ◀ · ▶ | 거꾸로 재생 · 재생(한 번/루프/왕복 모두 거꾸로 된다) |
| 재생 단위: 연속 / 1프레임 | 흐른 시간 그대로 · 보이는 시각을 정수 프레임(24fps)에 맞춘다(마야 Real-time). 속도는 둘 다 실제 시간을 따른다. 설정에 남는다 |

## 전역 단축키 (마야 타임 슬라이더)

창 어디에 포커스가 있든 듣는다(글 입력 칸만 예외). 오른쪽 "키 › 현재 시각" 줄에 같은 버튼이 있다.

| 키 | 뜻 (마야) |
|---|---|
| **1** · **2** | 이전 키 · 다음 키 |
| **Alt+1** · **Alt+2** | 이전 프레임 · 다음 프레임 (24fps, ←/→와 같다) |
| **`** | 첫 프레임(재생 범위 처음)으로 |
| **Alt+`** · **Alt+Q** | 재생 토글 (PlaybackToggle) — Space는 쓰지 않는다 |
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

## 씬 · 오브젝트 · 월드 축 (`docs/PLAN-scene.md` S1)

- 왼쪽 **씬 — 오브젝트** 하이어아키: `Scene › stage › layer › 오브젝트 › 리그 노드(연기 축 / 배치 축)`. 오브젝트 = 리그 인스턴스(슬롯). id·rigKind·레이어(far·back·mid·front·close)로 추가하고, 트리에서 클릭하거나 뷰포트에서 초상을 클릭해 고른다. 고른 오브젝트가 매니퓰레이터·모션 미리보기·초상 PNG의 대상이다. **노드를 클릭**하면 그 노드가 매니퓰레이터 노드·채널 추가 대상이 되고(키 따라가기는 꺼진다), **더블클릭**하면 그 노드에 채널이 생긴다(종류·mode는 "채널 추가" 콤보). 기본 씬은 `c1 · character` 하나다.
- 오른쪽 **오브젝트(채널 박스)**: X · Y · Rot Z · Scale X/Y · Layer · 보이기 — **배치**다(모션 키가 아니다). 값은 런타임이 움직이는 배치 축 노드(`CharSlot_Track` · `CharSlot_SwayPivot` · `CharSlot_Scale`)에 그대로 들어가고, 트리는 코어 `RigSchemaLoader.AddRigTo`가 슬롯 접두사(`c1/노드`)로 세운다 — 리듀서의 slot 폴드와 같은 함수.
- **월드 축·격자**: 루트 공간(가운데 원점·y 위·px). 빨강 x·초록 y 축, 가는 선 1u(기준 폭/48 = 40px), 굵은 선 5u, 무대 프레임. "격자·축"으로 끈다.
- **모션의 주인은 rigKind**다. 고른 오브젝트의 리그가 모션의 리그와 같거나 채널의 노드를 전부 가지면(공통 스키마) 그 오브젝트에 입혀 보인다. 아니면 "이 리그에 안 맞는다"로 표시된다.
- **깊이**: 덤프 옆 `presets/depth.json`(ExportedTuning)이 있으면 레이어(far·back·mid·front·close)가 깊이(DepthY·DepthScale)가 된다. 런타임 size 폴드와 같은 코어 함수(`SetDepthStageReduction`, focus 보존 bust)를 지나므로 보이는 깊이 = 정지 프레임의 깊이다. 없으면 레이어는 그리는 순서만 정한다.
- **카메라(보기)**: 채널 박스 아래 zoom · pan X/Y. zoom 1당 배율 +0.05(코어 규약), 보기만 바뀌고 씬에 남는다. 카메라 키는 규약에 없다.
- **모션 목록**은 rigKind 순으로 묶이고, 고른 오브젝트가 입을 수 없는 모션은 흐리게("안 맞음"). 툴바의 **내보내기 범위**: 전체 / 이 rigKind만 / 이 오브젝트가 입는 모션만 — 고른 모션의 문제만 내보내기를 막는다.
- 씬은 저작본 `scenes[]`에 남고(미리보기 전용) `motions.json`에는 들어가지 않는다. 예전 문서의 `preview.portrait`는 열 때 c1의 초상이 된다.

## 뷰포트

- **매니퓰레이터**: 오른쪽 "뷰포트 조작"에서 노드를 고르고 **W** 이동 · **E** 회전 · **R** 배율 · **Q** 선택. 기즈모의 축 끝은 한 축만, 가운데는 자유. 놓는 순간 **Auto Key**면 키가 들어가고(바뀐 성분만), 아니면 "키 없는 포즈"로 남아 **S**로 키를 놓거나 **Esc**/시각 이동으로 버린다. 그 시각에 이미 키가 있으면 바로 덮어쓴다. 채널·성분이 없으면 모션 기본 mode로 만든다.
- 리그 덤프(`rig-schemas.json`)로 세운 트리 위에 초상 PNG 한 장. 사이징은 런타임 `HeightFitPreserveAspect`와 같다. 깊이 mid, 카메라 없음(v1).
- 포즈 = 코어 `MotionMath.Compose`. 길이를 지나면 restore는 시작 상태, commit은 마지막 키 — 재생도 그 규칙을 보여 준다.
- **Alt+`** 재생/일시정지 · **`** 첫 프레임 · **Home/End** 처음/끝 · **←/→** 한 프레임(24fps). 타임 슬라이더·그래프의 현재 시각과 하나다.
- 어니언 스킨: 앞뒤 2프레임 반투명. 선택한 키의 노드에 피벗·x(빨강)·y(초록) 축.
- `base-resolution.json`은 리그 덤프 옆에서 자동으로 싣는다(없으면 1920×1080).

## 설정 · 저장

- 설정은 `%APPDATA%/ked-motion-tool/settings.json`(스냅·스냅 간격·새 키 탄젠트·어니언 프레임·Auto Key·시간 표시(프레임/초)·자동 저장 간격·최근 파일 8개). 창을 닫을 때 저장. 테마는 마야처럼 어두운 색 고정.
- 자동 저장은 문서 옆 `<이름>.autosave.motion-authoring.json`(기본 5분, 바뀐 것이 있을 때만). 저장 상태는 건드리지 않는다.
- 아래 문제 칸의 각 줄에 **돕는 손** 버튼: 끝 키를 길이로 · 길이를 마지막 키에 · 길이 자리에 키 넣기 · 첫/마지막 키를 중립값으로 · 첫 키를 0초로 · 채널을 relative로 · 끝을 commit으로. 각각 되돌리기 한 번.
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
