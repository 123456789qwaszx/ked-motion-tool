# 되돌리기 · 다시하기 — 조사와 결정 (2026-10-28)

소유자가 준 정적 분석(기준 커밋 `7164a0c`)을 최신 `dev`에서 재확인하고 7단계로 고쳤다. 저장 포맷(`motion-authoring/2`)·`motions/2`·런타임 규약은 그대로다.

## 1. 최신 코드에서 확인한 원인

| 항목 | 확인 | 처치 |
|---|---|---|
| CI 빌드 오류(`GraphEditorViewTests` `editor` 미선언) | 분석 뒤 이미 고침(d01f98d, CI 녹색) | 없음 |
| Ctrl+Z/Y가 그래프·도프시트 `OnKeyDown`(논리 키 `Key.Z`)에만 있고 셸·뷰포트·하이어아키에 없음, 툴바는 편집기 직접 호출 | 코드로 확인 | 2단계 |
| `Mutate`가 변화 여부와 무관하게 기록·다시하기 삭제, `OnCommit`이 Enter와 LostFocus 양쪽에서 확정 → 같은 편집이 두 번 | 코드로 확인 | 1·3단계 |
| `Mutate`가 스냅샷을 먼저 쌓고 명령이 던지면 반쯤 바뀐 문서와 칸이 남음(`Batch`만 복구) | 코드로 확인 | 1단계 |
| pending 포즈가 있을 때 S → `Viewport.SetKey` → 항목마다 `KeyNow` → `Mutate` 여러 번 · `EnsurePreviewModeForGroup`이 키와 별개 칸 | 코드로 확인 | 4단계 |
| 키 없는 포즈(`_overrides`·`_pending`·`_held`)는 문서 밖이라 이력 없음, 문서 Undo 뒤에도 화면에 남음, Esc·스크럽이 버리면 못 돌아옴 | 코드로 확인 | 5단계 |
| Undo는 `Restore → Raise(Structure)`만 하고 `RefreshAssets()`를 안 거쳐 초상 비트맵·리그·깊이·focus·컨트롤러 표가 문서와 어긋남 | 코드로 확인(헤드리스는 PNG를 못 읽어 참조·다시 싣기 횟수로 검증) | 6단계 |
| 깊이 한 번 = DepthScale X·Y + DepthY 한 칸 | 이미 `Batch`였다 | 없음(테스트만 추가) |

## 2. 설계

- **이력은 편집기 하나.** `MotionEditor`의 칸 = 문서 스냅샷(JSON) + 부수 손(undo/redo 델리게이트). `RecordExternal(undo, redo)`로 문서 밖 상태(뷰포트의 키 없는 포즈)를 같은 줄에 올린다. 묶음(`Batch`) 안이면 그 칸에 붙어 문서 변화와 함께 돌아온다. `IsRestoring` 동안 올라오는 손은 복원의 반응이라 버린다.
- **무변경 방어는 편집기가 한다.** `Mutate`/`Batch`가 조작 뒤 스냅샷을 전과 견줘 같으면 기록·다시하기 삭제·Revision 변경을 하지 않는다(탄젠트 재계산까지 실제 문서 기준). 예외가 나면 문서를 복구하고 칸을 남기지 않는다.
- **글 칸은 한 번만 확정한다.** `OnCommit`이 확정한 글을 기억해 포커스가 떠날 때 같은 글은 다시 확정하지 않고, 들어와서 안 바꾸고 나가면 아무 일도 없다. Enter로 확정하면 포커스를 뷰포트로 옮긴다 — 그 뒤 Ctrl+Z는 문서 되돌리기(마야 채널 박스). 칸 안에서는 글자 되돌리기가 우선이다(셸은 글 입력 칸의 키를 건드리지 않는다).
- **단축키는 셸 한 곳.** `OnShellKeyDown`(터널, `PhysicalKey`)이 Ctrl+Z · Ctrl+Y · Ctrl+Shift+Z를 받아 `Handled`로 막는다. 한글 입력 상태의 논리 키 불일치도 사라진다. 툴바 버튼도 같은 `UndoCommand/RedoCommand`.
- **조작 하나 = 한 칸.** 드래그(`EndManipulation` + 포즈 차이), 채널 박스 값(`SetComponents`), S(`SetKey` 전체), ●(`KeyComponents`), 컨트롤러 값·키의 재생 기준 자동 변경, 깊이 셋 — 모두 `Batch` 안.
- **키 없는 포즈 정책(추천안 채택).** pending 상태의 모든 변화가 이력이다: 끌기·값 입력으로 생긴 것, Esc로 버린 것, 스크럽·선택 변경·재생·모션 전환으로 사라진 것 전부 Ctrl+Z로 돌아오고 Ctrl+Y로 다시 버려진다. 되살아난 포즈는 그 시점의 현재 시각에 얹힌다(포즈는 시각과 무관한 노드 상태). S로 키한 것을 되돌리면 키 확정 직전의 포즈로 돌아온다. 문서를 갈아 끼우면(새로/열기) 포즈도 이력도 조용히 비운다. 저장 파일·산출물에는 아무것도 안 들어간다.
- **자산은 참조 지문으로.** `DocumentSession`이 편집기 `Changed`(Keys·Metadata 제외)를 듣고 리그 덤프 경로·기준 해상도 경로·오브젝트별 초상의 지문을 견줘, 달라진 때만 다시 싣고 `AssetsChanged`를 올린다(리그가 바뀌면 깊이·focus·컨트롤러 표도). `LoadRig`·`SetPortrait`처럼 직접 싣는 길은 반응하지 않는다. 비트맵은 전처럼 `ClearPortraits` 뒤 다시 만든다.

## 3. 회귀 테스트 (Avalonia Headless, 실제 포커스·키 입력·확정 경로)

- `MotionEditorTests`: 같은 값 명령은 기록·다시하기 삭제·Revision 없음 · 던지는 명령은 흔적 없음 · 문서 밖 조작의 순서 · 묶음에 붙은 손.
- `MainWindowTests`: 네 곳 포커스에서 Ctrl+Z/Y/Shift+Z가 정확히 한 번, 글 입력 칸의 Ctrl+Z는 문서를 안 건드림 · 값 입력 → Enter → 다른 패널 → Ctrl+Z 한 번, 같은 값 확정·포커스 이동으로 다시하기 유지, 새 편집은 다시하기 삭제 · 좌우·상하 pending → S 한 칸(되돌리면 pending 복원), Auto Key OFF 깊이 → S에서 DepthScale X·Y·DepthY 동시 복원, 첫 키 + 재생 기준 동시 복원 · 초상 Undo/Redo 자산 동기화, 키 편집은 자산을 안 읽음, 리그 참조 Undo/Redo.
- `ViewportViewTests`: 포즈 Undo/Redo · S 되돌리면 포즈 · Esc·스크럽으로 버린 포즈 복귀 · 문서 조작과 순서 · 문서 교체 뒤 포즈 없음.

## 4. 검증 한계

이 환경에는 `dotnet`이 없어 빌드·테스트는 GitHub Actions로만 확인했다. 실제 한글 IME 입력과 마우스로 패널을 클릭하는 포커스 이동은 헤드리스로 흉내만 냈다(`KeyPressQwerty`·`Focus()`). 헤드리스는 PNG를 못 읽어 초상 동기화는 참조와 다시 싣기 횟수로 검증했다.
