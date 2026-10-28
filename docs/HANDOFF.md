# ked-motion-tool 인수인계 (2026-10-28, 1차 완성 시점)

이 문서는 다음 담당자가 **반나절 안에 전체 그림을 잡고 바로 손댈 수 있게** 쓴 인수인계다.
사용법은 [`USER-GUIDE.md`](USER-GUIDE.md), 유지보수 절차는 [`MAINTENANCE.md`](MAINTENANCE.md),
설계 결정의 근거는 [`PLAN.md`](PLAN.md)와 그 하위 계획서가 정본이다. 셋이 충돌하면 코드와 테스트가 이긴다.

## 1. 무엇인가

캐릭터 모션 전용 저작 도구(마야식 그래프 편집기 + 뷰포트 + 도프 시트). 입력은 유니티가 내보낸
리그 덤프(`ExportedTuning/`)와 초상 PNG, 산출물은 무대 런타임(`ked-stage-runtime`)이 읽는
`motions.json`(스키마 `motions/2`) **하나**다. 툴은 Yarn·커맨드·대본을 모른다.

- 저장소: `123456789qwaszx/ked-motion-tool`, 작업 브랜치 **`dev`**, CI는 `main`·`dev` 푸시와 PR에서 돈다(Linux, `dotnet test`).
- 스택: .NET 10 SDK(`global.json` 10.0.100) · Avalonia 12.1.1 · xunit + Avalonia.Headless · 반입 코어 테스트는 NUnit.
- 상태: **1차 완성(소유자, 2026-10-28)**. M0~M5 완료, 이후 사용 피드백으로 손본 것까지 `dev` 37b5d3d. CI 녹색.

## 2. 소유자가 정한 것 (다시 논하지 않는다)

| 결정 | 뜻 | 근거 문서 |
|---|---|---|
| 독립 Avalonia 앱, Core는 **복사 반입** | `src/Ked.Presentation.Core`는 `ked-stage-runtime`의 사본. 정본은 저쪽, 흐름은 한 방향. 이쪽 사본을 고치지 않는다 | `src/Ked.Presentation.Core/vendoring.md` |
| 진실은 저작 원본 | `*.motion-authoring.json`(`motion-authoring/2`)이 주인, `motions.json`은 내보내기 산출물. 손으로 JSON을 고치지 않는다 | `PLAN.md` §1·§5 |
| 리그는 만들지 않는다 | `rig-schemas.json`(유니티 덤프)에서 `RectNodeTree`를 세운다. 덤프 없이도 내장본으로 열린다 | `PLAN.md` §4, `Mt.Authoring/Rig/BuiltinTuning` |
| 보이는 것 = 재생하는 것 | 포즈는 매 프레임 코어 `MotionMath.Compose`. 재현이 아니라 같은 함수 | `PLAN.md` §1 |
| 채널에 mode는 없다(`motions/2`) | relative/absolute는 대본의 `<<motion slot 이름 relative\|absolute>>`가 정한다. 툴의 "재생 기준"은 키 값을 읽는 렌즈일 뿐 파일에 안 나간다 | `ked-stage-runtime/docs/motion-contract.md` |
| 컨트롤러 표 | 애니메이터가 리그 계층을 몰라도 되게 `controllers.json`(`controllers/1`)이 자리·깊이·연기 세 묶음의 손잡이를 정한다. 덤프 옆 `presets/controllers.json`이 있으면 그것이, 없으면 내장본 | `PLAN-controller.md` |
| 리그 하나의 모션에 집중 | 배역(character)·오브젝트 부모/자식·메모·채널 mode UI·그래프의 절대/정규화/쌓기·기울기 칸은 **통째로 제거**했다(마이그레이션 없음 — 옛 저작본이 없다). 불필요한 기능은 흔적 없이 지운다 | 커밋 30b0c8e·429790e·c698d4d·7c82677 |
| 설정은 인스펙터가 아니라 창 | 오른쪽 패널은 채널 박스·씬·모션 탭 셋. 설정·단축키(F1)는 툴바의 창 | `PLAN-inspector.md` |
| 조작 하나 = 되돌리기 한 칸 | 키 없는 포즈도 이력에 든다. 무변경 조작은 기록하지 않는다 | `PLAN-undo.md` |
| 단축키는 마야 | 전역 단축키·그래프 조작·타임 슬라이더 전부 마야 관습. 그림자 버튼은 두지 않는다 | `PLAN-maya.md`, `maya-comparison.md` |

## 3. 구조 한눈에

```
Ked.Presentation.Core (복사 반입, 수정 금지)
   └─► Mt.Authoring (도메인 — Avalonia를 모른다)
           Model/        MotionDocument·MotionAsset·ChannelAsset·SceneAsset·StageNodes
           Editing/      MotionEditor — 모든 편집의 유일한 통로, 스냅샷 undo(최대 200)
           Serialization MotionDocumentJson(저작 원본) · MotionsJson(motions/2 정규 쓰기·관용 읽기)
           Validation/   MotionValidator(코어 MotionRules) · MotionFixes(돕는 손) · MotionExport
           Rig/          RigSchemaFile(덤프+SHA-256) · BuiltinTuning · ControllersFile · DepthTuningFile · FocusTuningFile · BaseResolutionFile
           Preview/      ScenePreview(씬 조립) · MotionPreview(포즈) · PoseEdit · PortraitLayout · StageShotResponse · PlaybackClock
           Graph/        순수 함수 — 좌표·히트·탄젠트·스냅·키 연산·선택·클립보드·도프 행
           Presets/      프리셋 모션 9종
           Settings/     ToolSettings(%APPDATA%/ked-motion-tool/settings.json)
   └─► Mt.App (그리기와 입력)
           MainWindow    셸 — 모션·하이어아키·인스펙터 탭·툴바·전역 단축키·설정/단축키 창
           Views/        ViewportView · GraphEditorView · DopeSheetView · TimeSliderView · RangeSliderView
           Services/     DocumentSession — 경로·리그 덤프·자산 동기화·저장·내보내기·자동 저장
tests/  Ked.Presentation.Core.Tests(NUnit, 원본 그대로) · Mt.Authoring.Tests(xunit) · Mt.App.Tests(Avalonia.Headless)
tools/  prefab-to-stage-rig.py(유니티 프리팹 → 무대 뼈대 JSON) · prefab-tree.py
```

핵심 불변식 셋.
1. **편집은 `MotionEditor`만 지난다.** 뷰는 모델을 직접 만지지 않는다. 드래그 중간값은 렌더만, 놓는 순간이 편집 하나(`Batch`).
2. **Core 사본은 읽기 전용.** 소비자 쪽(`Mt.Authoring`·`Mt.App`)에서 적응한다. 사본을 고치면 다음 동기화가 조용히 지운다.
3. **산출물 등가.** 같은 입력이면 유니티 `MotionClipExporter`와 바이트까지 같다. `nod`·`bow` 골든 테스트가 지킨다.

## 4. 데이터와 규약

| 파일 | 스키마 | 주인 | 비고 |
|---|---|---|---|
| `*.motion-authoring.json` | `motion-authoring/2` | 이 툴 | 모션·채널·키·탄젠트·씬(`scenes[]`)·리그 덤프 참조(경로+해시)·초상 참조. 옛 `/1`은 읽힌다(채널 `mode` 버림, `defaultMode` → 재생 기준) |
| `motions.json` | `motions/2` | 무대 런타임 규약 | 모션 이름·길이·`end`(restore/commit)·채널(노드×종류×성분 키). 정규 직렬화(§7)라 diff가 안정. 옛 `/1`은 읽힌다 |
| `ExportedTuning/rig-schemas.json` | 유니티 덤프 | 런타임 | 노드 id·parent·앵커·피벗·크기. 내장본은 `Mt.Authoring/Defaults/ExportedTuning` |
| `ExportedTuning/base-resolution.json` · `presets/depth.json` · `presets/focus-tuning.json` | 유니티 덤프 | 런타임 | 무대 크기 · 깊이 커브 · focus 보존 표 |
| `presets/controllers.json` | `controllers/1` | 리거(이 툴) | 손잡이 표. 내장본 `Defaults/controllers.json` |
| `Defaults/stage-skeleton.json` | 툴 내부 | 이 툴 | `PresentataionUIRoot.prefab`에서 뽑은 85노드 무대 뼈대. `tools/prefab-to-stage-rig.py`로 재생성 |
| `settings.json` | 툴 내부 | 사용자 | `%APPDATA%/ked-motion-tool/` |

규약의 출처: `ked-stage-runtime/docs/motion-contract.md`. 규약이 바뀌면 **코어 재반입 → `MotionsJson` 적응 → 골든 테스트 갱신** 순이다(`MAINTENANCE.md` §4).

## 5. 연관 저장소

| 저장소 | 관계 | 지금 상태 |
|---|---|---|
| `ked-stage-runtime` | 코어 정본(`Assets/Ked.Presentation/Core`), `motions/2` 로더·`motion` 커맨드 보유 | `dev` @ 640ac60이 반입 기준. 배경 리그 제거는 `refactor/background-as-slot` @ dc690c1에 있고 **`dev`에는 아직 없다**(2026-10-29) — `dev`가 품으면 코어 재반입(MAINTENANCE §4.1). 툴 산출물 등가 테스트는 §7-1 |
| `ked-presentation-runtime` | 반입본 구조의 재생 저장소. 샘플 `Assets/Samples/Dialogue/motions.json`·초상 PNG | 배경 리그 제거가 `dev`에 들어갔다(무대 `refactor/background-as-slot` @ dc690c1 반입, 2026-10-29). 데모 `motions.json`은 여전히 툴 견본 `motions.nod-bow.json`과 바이트까지 같다 — 데모 대본은 `<<motion s1 nod>>`·`<<motion s1 bow absolute>>` |
| `VNStoryEditor_avalonia`(VnTool) | 작가용 시나리오 툴. 2026-10-28에 무대 프리뷰·에셋 탐색기·연출 커맨드 편집 UI를 제거했다. 연출 저작은 별도 툴이 맡기로 | `dev` @ 3c2d922. 이 툴과 데이터 교환 없음(앞으로 `motions.json`의 이름 목록만 읽을 가능성) |

## 6. 최근에 바뀐 것 (사용 피드백 반영, 2026-10-28)

최신부터. 전부 `dev`에 있고 CI 녹색이다.

- 매니퓰레이터: 이동은 화살표, 배율은 끝 상자, 회전은 고리. 손잡이는 노드 원점이 그림 밖이면(발 기준 회전의 SwayPivot) 그림 가운데에. 배율은 팔 방향 투영이라 가운데를 넘어도 커지지 않는다. 무대 밖 오브젝트도 그리고 바깥은 회색.
- 씬 카메라(샷) pan·zoom이 자리 있는 오브젝트에 안 먹히던 버그 — 샷을 오브젝트 조립 뒤로.
- 타임 슬라이더 키 틱을 현재 프레임 상자 위에. 그래프 키 라벨은 값만. 그래프 팬은 RMB만, 뷰포트 팬은 MMB·RMB.
- 되돌리기 7단계(`PLAN-undo.md`): 셸 한 경로, 무변경 조작 무기록, 복합 조작 한 칸, 키 없는 포즈 이력, 자산 동기화.
- 채널 박스 이름(발 기준 회전·좌우/상하·배율), 깊이 슬라이더 폭·최대 40, 그래프 툴바 정리, 시간 스냅 기본 1/24초.
- 제거: 배역, 오브젝트 부모/자식, 메모, 채널 mode UI, 그래프 절대/정규화/쌓기·기울기 칸.

## 7. 열린 항목 — 다음 담당자가 집을 일

우선순위 순.

1. **런타임 실제 적용(M6 일부) — 툴 쪽 끝, 무대 쪽 Unity 확인 남음.** 견본 `tests/Mt.Authoring.Tests/Fixtures/stage-parity/`(`motions.json` + 포즈 표 `poses.json`)가 두 저장소를 잇는다. 툴 쪽 `StageParityTests`(CI)가 ① 편집기로 지어 내보낸 바이트 = 견본 ② 미리보기 포즈 = 표 ③ 반입 코어 리듀서가 접은 끝 상태 = 표를 지킨다. 무대 쪽 `UnityParity/ToolExportMotionParityTests`가 같은 두 파일을 런타임 로더(`MotionLibrary`, JsonUtility)·리그 스키마·리듀서로 읽어 같은 표를 확인한다 — 무대 저장소에는 CI가 없어 소유자가 Unity Test Runner로 돌린다. 시간에 따른 재생(`MotionCommandCharR`, DOTween)은 플레이모드라 범위 밖.
2. **VnTool 연결(M6 나머지).** VnTool 쪽 `<<motion>>` 자동완성이 `motions.json`의 이름·길이·end 목록만 읽게. 이 툴은 바뀔 것이 없다.
3. **Windows 휴대용 패키지.** 지금 CI는 Linux 빌드·헤드리스 테스트뿐. VnTool처럼 `publish-windows.ps1` + windows-latest 스모크를 더할 때가 되면 `.github/workflows/test.yml`에 잡을 추가한다.
4. **알려진 한계(`PLAN-undo.md` §4).** 설정 창·단축키 창은 헤드리스에서 두 번째 창이 멈춰 테스트하지 않는다(08df523). PNG 디코딩도 헤드리스에서 안 된다 — 초상 관련은 자리표시 사각형으로 검증한다.
5. **v2 범위(짓지 않는다, 발목만 안 잡는다).** 카메라·깊이·shot 채널, 다중 스프라이트 리그, 연출 클립 — `PLAN.md` §8.

## 8. 작업 관습 (소유자와 맞춘 것)

- 작업 언어는 한국어 존댓말. 코드 주석·문서도 한국어. 커밋 제목은 "무엇을 왜" 한 줄 + 본문.
- **한 번에 하나.** 버그·사용성 하나씩 고치고 CI 녹색을 본 뒤 다음으로. 구조는 유지하고 마야식 편의를 더한다.
- 안 쓰는 기능은 **흔적 없이** 지운다 — 모델·편집기·화면·테스트·문서까지. 마이그레이션은 두지 않는다(옛 저작본이 없다).
- 단축키는 F1 창의 표 하나로 충분하다. 그림자 버튼을 두지 않는다. 설정은 인스펙터에 넣지 않는다.
- 커밋 전에 `dotnet build`·`dotnet test`(로컬에 SDK가 없으면 CI로). `TreatWarningsAsErrors`라 경고 하나가 빌드를 깬다.
- 되돌리기 정책·편집 통로·Core 반입 규칙은 깨지면 안 되는 불변식이다(§3).
