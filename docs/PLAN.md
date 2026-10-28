# 모션 툴 계획 — ked-motion-tool

> 원본: `ked-stage-runtime/docs/PLAN_motion-tool.md` (ec9ada2)에서 옮겨 왔다. 이제부터 **이 파일이 정본**이다 — 저쪽 사본은 다음 손질 때 이쪽을 가리키는 한 줄로 줄인다.

기준: ked-stage-runtime `dev` @ `64e7679` · `docs/motion-contract.md`(motions/1) · VnTool(`VNStoryEditor_avalonia` @ `6630b26`)
작성: 2026-10-28

**현재 위치:** 모션 경로는 Unity Animation 창으로 끝에서 끝까지 확인됐다(`nod`·`bow`). 이 문서는 **그 길을 버리지 않고**
그 위에 전용 저작 도구를 세우는 계획이다. 다음 작업은 M0.

---

## 0. 왜 전용 툴인가, 왜 지금 Unity 창을 버리지 않는가

Unity Animation 창은 **되긴 하지만 저작 도구가 아니다.** 겪은 것만 적는다.

| 한계 | 결과 |
|---|---|
| 위치·회전·배율이 세 성분 묶음 — 한 축만 지울 수 없다 | x·y 0→0이 따라 나온다(내보내기가 relative에서만 걸러 준다) |
| 도프시트와 커브가 한 창의 두 모드, 다중 커브 겹쳐 보기·값 범위·팬·줌이 약하다 | 마야 그래프 에디터의 감각이 안 난다 |
| 탄젠트 모드(Auto·Flat·Linear·Broken·Weighted)가 메뉴 깊숙이 있고, 우리 규약 밖(Weighted·Constant)을 막지 못한다 | 내보내기에서야 거부된다 |
| 리그 밖 환경(깊이·카메라)이 없다 — 제작 리그는 캔버스 아래 리그뿐 | v2가 필요로 하는 것이 애초에 없다 |
| 되돌리기·스크럽·재생이 Unity 전체 편집기의 것이다 | 가볍게 여러 모션을 오가며 만드는 흐름이 무겁다 |

그래도 Unity 길은 남긴다. 셋 때문이다.
1. **규약과 수학이 도구와 무관하다** — `motions/1` · `MotionRules` · `MotionMath`는 Core에 있고, 어느 도구가 만들든 같은 파일·같은 재생이다. 이게 지금까지 한 일의 요지다.
2. **Unity 내보내기가 심판이다** — `AnimationClip.SampleAnimation ↔ MotionMath.Compose` 대조가 이미 있다. 툴이 만든 파일과 Unity가 만든 파일이 **바이트까지 같아야** 한다(§6).
3. 툴이 서기 전까지의 생산 수단이고, 툴이 선 뒤에는 "이상하면 Unity로 열어 본다"의 교차 검증이다.

**5단계(카메라·의도 채널)보다 툴이 먼저다.** 카메라·깊이는 "모션이 무엇을 움직이는가"의 확장이고, 그걸 만들 자리가 툴이다.
Unity 창에는 그 자리가 없다. 다만 **v1은 지금 규약 그대로** — 캐릭터 리그 하나, 노드 채널만. 그것만으로 사용감을 내는 것이 어렵고, 거기서 확고해져야 v2가 선다.

---

## 1. 결정 (다시 논하지 않는다)

| | 결정 | 근거 |
|---|---|---|
| 형태 | **독립 Avalonia 앱**, 새 저장소 `ked-motion-tool`. .NET 10 + Avalonia 12(VnTool과 같은 판) | VnTool 코드를 **들어올 수** 있다(참조가 아니라 이식). 같은 언어라 Core를 그대로 쓴다 |
| Core | `Ked.Presentation.Core`를 **복사 반입**(VnTool과 같은 방식, `vendoring.md`) | 툴의 의존은 Core + 데이터뿐 — Unity 내보내기와 같은 조건. 무대 런타임은 툴을 모른다 |
| 진실 | 툴의 **제작 원본 파일**(`*.motion-authoring.json`, §5)이 주인. `motions.json`은 내보내기 산출물 | Unity 길의 AnimationClip + MotionDefinition과 같은 자리. 손으로 JSON을 고치지 않는다 |
| 리그 | `rig-schemas.json`(부모 사슬·앵커·피벗·크기)에서 `RectNodeTree`를 세워 `RectChainMath`로 그린다 | 폴드가 보는 것과 같은 리그. Unity도 프리팹도 필요 없다. 스키마 해시를 원본 파일에 적어 어긋나면 경고 |
| 미리보기 | 매 프레임 `MotionMath.Compose`, 끝 상태는 `EndValue` | 보이는 모양 = 재생하는 모양 = 정지 프레임. 재현이 아니라 **같은 함수**다 |
| VnTool과의 관계 | VnTool은 `motions.json`의 **이름·길이·end 목록**만 읽는다. 툴은 Yarn·커맨드·대본을 모른다 | 책임 분리(2026-10-27 리뷰). VnTool의 모션 그래프(MG, 보류)는 커맨드 텍스트의 렌즈라 **다른 물건**이고, 그 문서가 독립 앱을 기각한 이유 ②③(왕복 무손실을 사람 손에 맡김·프리뷰 중복)은 자기 원본을 가진 이 툴에 해당하지 않는다 |
| v1 범위 밖 | 카메라·깊이·shot · 다중 스프라이트 리그 · 스프라이트 교체 · 이벤트 · 여러 커맨드의 타임라인 · 컷인·배경·이모지·화면전환 | §8. 규약이 노드 기반이라 리그가 늘면 따라온다 — 지금 설계에 발목 잡히지 않는다 |

---

## 2. v1 범위 — "단순한 캐릭터 모션 툴을 확고하게"

캐릭터 리그 하나. 한 번에 모션 하나를 연다. 채널은 노드 × 종류(position·scale·euler)에 성분 x·y·z.
relative/absolute · restore/commit · 길이 · 키 · 탄젠트. 가져오기/내보내기 `motions/1`. 초상 PNG를 골라 보면서 만든다.

**사용감의 기준은 마야 그래프 에디터다.** 아래가 v1에 있어야 "확고하다"고 부른다.

### 2.1 그래프 에디터 (M2·M4)
- 여러 커브 겹쳐 보기. 채널·성분마다 색. 보이기/잠금 토글. 정규화 보기(각 커브를 0~1로 눌러 모양만 비교) ↔ 절대값 보기
- 팬(가운데 버튼·Alt+드래그) · 줌(휠, 축별 줌) · **F**(선택 맞춤) · **A**(전체 맞춤) · 자동 값 범위
- 키: 클릭 선택 · 박스 선택 · Shift 추가 · 드래그(시간·값, 축 잠금 Shift) · **I**(현재 시각에 키 삽입, 값은 곡선 위) · Delete · 인스펙터에서 시각·값 직접 입력
- 시간 스냅: 1/60초 기본(24fps 눈금 병기 — `DurationToken`과 같은 규약), 끄기 가능. 값 스냅 없음
- 탄젠트: Unified/Broken · Auto · Flat · Linear · 핸들 드래그. **Weighted·Stepped는 없다**(규약 밖 — 메뉴에 두지 않는다)
- 다중 키 이동·값 스케일(선택을 기준점으로 늘리기) · 시간 스케일
- 되돌리기/다시하기 전부. 드래그 중간값은 렌더만, 확정(마우스 업)이 편집 하나(VnTool 성능 규칙 그대로)

### 2.2 도프시트 (M4)
- 왼쪽 트리: 노드 › 종류 › 성분. 키 블록은 요약 행(노드)·세부 행(성분) 양쪽에
- 블록 선택·이동·시간 스케일·복제. 그래프와 **선택이 같다**(한 모델, 두 뷰)
- 채널은 따로 추가하지 않는다: 하이어아키에서 노드를 고르고 채널 박스에 값을 넣거나 매니퓰레이터로 끌면 첫 키에서 채널·성분이 생긴다 — Unity처럼 셋이 묶여 따라오지 않는다

### 2.3 뷰포트 (M3)
- `rig-schemas.json`으로 세운 리그, 기준 해상도 무대, mid 깊이(DepthScale 1). 초상 PNG 하나
- 재생·일시정지·루프·스크럽, 현재 시각·프레임 표시
- 어니언 스킨(앞뒤 몇 프레임 반투명) · 노드 축 표시(선택 노드의 피벗·축)
- restore면 끝에서 시작 상태로, commit이면 마지막 키로 — 재생도 그 규칙을 보여 준다

### 2.4 검증 (M1·M5)
- `MotionRules`를 **편집 중 계속** 돌린다. 어긋난 것은 패널에 런타임과 같은 문구로. 내보내기는 0건일 때만
- 돕는 손: "마지막 키를 중립값으로"(restore 경고) · "길이를 마지막 키에 맞추기" · "끝 키를 길이로 옮기기" · "끝을 commit으로"
- (motions/2) 키가 있는 성분은 전부 내보낸다 — 파일이 mode를 모르므로 중립 상수를 뺄 수 없다. restore인데 마지막 키가 중립값이 아니면 **경고**(막지 않는다)

---

## 3. 아키텍처 — VnTool의 틀을 그대로

```
Ked.Presentation.Core (복사 반입)  ──►  Mt.Authoring  ──►  Mt.App (Avalonia)
  규약·수학·리그 트리·폴드            제작 원본 모델 · 편집 명령 · undo   그래프 · 도프시트 · 뷰포트 · 인스펙터
                                       가져오기/내보내기 · 검증 · 정규 직렬화
tests: Ked.Presentation.Core.Tests(그대로) · Mt.Authoring.Tests(xunit) · Mt.App.Tests(Avalonia.Headless)
```

- **`Mt.Authoring`은 Avalonia를 모른다.** 편집은 전부 `MotionEditor`(VnTool의 `ProjectEditor` 자리)의 명령으로 수렴하고, 되돌리기 한 번 = 조작 하나.
- **`Mt.App`은 그리기와 입력뿐이다.** 좌표 변환·히트 테스트·핸들 산수는 순수 함수로 두고 테스트한다(VnTool `EaseCurveEditorMathTests`의 방식).
- 프로젝트 접두사 `Mt.`는 제안이다. 저장소 이름과 함께 정한다.

### 3.1 VnTool에서 들어오는 것 (이식 — 참조 아님)

| VnTool | 툴에서 | 손볼 것 |
|---|---|---|
| `Views/EaseCurveEditor.cs` | 그래프 에디터의 씨앗 | 커브 하나·정규화 0~1·끝 키 잠금 → 여러 커브·초 단위·값 범위·팬/줌·박스 선택·탄젠트 모드 |
| `Flow/StageSceneComposer.PlaceCorePortraits` + `RectChainMath` | 뷰포트의 초상 배치 | 무대 상태 대신 모션 합성 결과(노드 트리를 매 프레임 갱신)를 먹인다 |
| `Flow/EaseCurveBaker` | "표준 이징을 키로 굽기" 프리셋 | 그대로 |
| `Vn.Authoring`의 `ProjectEditor`·undo·`Mutate` 규율 | `MotionEditor` | 도메인만 바꾼다 |
| `Vn.App.Tests`의 헤드리스 셸 테스트 | `Mt.App.Tests` | 그대로 |
| `vendoring.md`·`three-repo-map` 관습 | Core 반입 기록 | 그대로 |

들어오지 않는 것: 커맨드·Yarn·엑셀·챕터·무대 상태 폴드(`CoreStageFold`). 툴은 그것들을 모른다.

---

## 4. 리그와 환경을 보장하는 방법

모션은 특정 리그에 묶인 데이터다. 툴은 리그를 **코드가 아니라 데이터로** 안다.

1. 제작 원본 파일이 `rig-schemas.json`의 경로와 **해시**를 적는다. 열 때 해시가 다르면 "리그가 바뀌었다 — 모든 채널의 노드·계층을 다시 검증했다 / 다음 n개가 사라졌다"를 띄운다. 조용히 맞는 척하지 않는다.
2. 노드 목록·계층·앵커·피벗·크기는 전부 그 파일에서 온다. 툴에 하드코딩된 노드 이름은 **없다**(`Track_Focus` 금지 같은 규칙은 Core `MotionRules`의 것이다).
3. 환경(기준 해상도)은 `base-resolution.json`에서. 깊이·카메라는 v1에 없다 — mid 고정이고 그렇게 표시한다.
4. 새 리그 종류(`rigKind`)가 덤프에 생기면 툴은 그 리그로도 만든다. 바뀌는 것은 없다 — 노드가 다를 뿐이다.

---

## 5. 데이터

### 5.1 제작 원본 — `*.motion-authoring.json` (schema `motion-authoring/2`)

```
{ schema, rigSchemas: { path, sha256 }, baseResolution: { path },
  preview: { portrait: "assets/harin_a_01.png" },
  motions: [ { name, rig, duration, end, previewMode,
               channels: [ { node, kind,
                             x|y|z: { keys: [ { t, v, inTangent, outTangent, tangent: "auto|flat|linear|free", broken: bool } ] } } ],
               notes } ] }
```
키의 수치는 `motions/2`와 같고, `tangent`·`broken`·`previewMode`(뷰포트의 렌즈 — relative/absolute는 재생 커맨드가 정하므로 파일에 안 나간다)·`notes`·`preview`·`scenes`가 제작 정보다. 내보내기는 그것을 벗긴다. 옛 `motion-authoring/1`은 읽힌다(채널 `mode` 버림, `defaultMode` → `previewMode`).

### 5.2 내보내기 — `motions/2` (규약 그대로)

**정규 직렬화를 규약에 올린다.** 필드 순서·2칸 들여쓰기·invariant `"R"` 포맷·UTF-8(BOM 없음)·LF — Unity 내보내기(`MotionClipExporter.ToJson`)가 지금 내는 그 모양. 두 생산자가 같은 키에서 **같은 바이트**를 내야 "둘 중 무엇으로 만들었는지가 파일에 남지 않는다"(§6). `motion-contract.md`에 §7로 적는다.

### 5.3 가져오기

`motions/2`(와 옛 `motions/1` — `mode`는 무시하고 알린다)를 읽어 제작 원본으로 올린다(탄젠트 모드는 `free`). Unity로 만든 `nod`·`bow`를 툴에서 열어 이어 만드는 길이고, 반대도 된다.

---

## 6. 심판 — 툴이 맞다는 것을 어떻게 아나

| 심판 | 무엇을 고정하나 |
|---|---|
| Core 테스트(반입본 그대로) | 수학·규칙이 런타임과 같다 |
| **바이트 동일** | 재생 저장소의 `Samples/Dialogue/motions.json`(`nod`·`bow`, Unity가 냄)을 툴로 열어 그대로 내보내면 **같은 바이트** |
| Unity 대조 | 툴로 만든 모션을 Unity 내보내기가 낸 것과 비교 — 같은 키면 같은 파일. 다르면 탄젠트 산수가 다른 것이다(아래 위험) |
| 재생 저장소 하네스 | 툴로 만든 모션으로 바꿔 끼운 데모에서 불일치 0 |
| 헤드리스 UI 테스트 | 클릭·드래그·단축키 → 편집 명령 → 모델. 좌표 산수는 순수 함수 테스트 |

---

## 7. 마일스톤

| | 내용 | 끝나면 |
|---|---|---|
| **M0** 뼈대 — 완료 | 저장소 `ked-motion-tool` · 솔루션 · Core 반입 + `vendoring.md` · 빈 창 · 헤드리스 테스트 1개 · CI(`dotnet test`) | 열리고 테스트가 돈다 |
| **M1** 데이터 — 완료 | 제작 원본 모델 · `MotionEditor`(undo) · 가져오기/내보내기 · 정규 직렬화 · `MotionRules` 연결 | `nod`·`bow` 왕복 바이트 동일 테스트 통과 |
| **M2** 그래프(단일 커브) — 완료 | `EaseCurveEditor` 이식 → 초 단위·값 범위·팬/줌·키 추가/삭제/드래그·탄젠트 모드·스냅 | 키보드·마우스만으로 `nod`를 처음부터 만든다 |
| **M3** 뷰포트 — 완료 | `rig-schemas.json` → 트리 → 초상 배치 · 스크럽·재생·루프 · restore/commit 끝 처리 · 어니언 스킨 | 만들면서 본다 |
| **M4** 도프시트·다채널 — 완료 | 노드/채널 트리 · 채널 추가 · 블록 이동/스케일 · 그래프와 선택 공유 · 여러 커브 겹쳐 보기 | `bow`(회전) + 위치 + 배율을 한 모션에 |
| **M5** 사용감 — `PLAN-M5.md`, M5a–f 완료 | 단축키 전부 · 인스펙터 · 검증 패널과 돕는 손 · 자동저장 · 이징 프리셋 굽기 · 설정 | 연출자가 Unity를 안 연다 |
| **M6** 연결 | 재생 저장소 데모의 `motions.json`을 툴로 재생산(바이트 동일) · VnTool `<<motion>>` 자동완성(그쪽) · 하네스 0 | 파이프라인이 닫힌다 |

M2가 **가장 어렵고 가장 중요하다.** 여기서 "마야 같다"가 결정된다. M3·M4는 M2의 그래프를 가정한다.

---

## 8. v2 이후 (지금 짓지 않는다 — 발목만 안 잡는다)

| | 무엇 | 선행 |
|---|---|---|
| **카메라·깊이·shot** | 모션에 카메라 채널(zoom·pan)과 의도 채널(focus 프리셋·ScreenPoint) | 소유권 규칙 — `ShotIntentState`를 매 프레임 전부 쓰는 응답 시스템과 두 쓰는 사람이 생긴다. `PLAN_stage-seam.md` §5 2단계. 툴에는 깊이 층·카메라 뷰가 필요하다 |
| **다중 스프라이트 리그** | 머리·몸·팔이 따로 — 새 `rigKind` | 무대 런타임의 빌더·초상·리듀서 폴드. 규약·툴은 노드가 늘 뿐 |
| ~~**mode를 커맨드가 정함**~~ — **했다(2026-10-28, motions/2)** | 모션은 값만 갖고 `motion slot 이름 [relative\|absolute]`가 정한다. relative = 지금 값 + 키(첫 키가 0이 아니어도 된다) | 무대 `motion/motions-2` @ `40d19d7`(코어 `MotionChannel.Mode` 제거 · `MotionModeToken` · 중립값 규칙 제거 · restore는 재생이 시작값을 되씀), 재생 반입본 `motion/motions-2` @ `6d72458`, 이 툴은 코어 재반입 + `motions/2` + 미리보기 mode |
| **연출 클립** | 컷인·배경 연출·감정 이모지·복합 화면전환 — 여러 리그·효과·시간차 | 모션 규약 밖이다(모션은 리그 노드의 변환뿐). 별 규약(`scene-clip/1`?)과 별 커맨드. 툴의 타임라인은 재사용된다 |

v1 설계가 이것들을 막지 않는지 M1에서 한 번 더 본다 — 채널이 "노드 × 속성"이라는 것, 시간이 초라는 것, 리그가 데이터라는 것 셋이 열쇠다.

**씬·오브젝트·월드 축·리그 선택**(2026-10-28 소유자 요청)의 조사와 계획은 [`PLAN-scene.md`](PLAN-scene.md)에 있다 — 코어 `StageState`(슬롯·레이어·샷)를 씬의 저작본으로 삼고, 모션의 주인은 rigKind로 둔다.

---

## 9. 위험

| 위험 | 대응 |
|---|---|
| **Auto 탄젠트 산수가 Unity와 다르다** → 같은 키인데 파일이 다르다 | 파일에는 탄젠트 **값**이 적힌다. 재생 등가는 깨지지 않는다. 바이트 동일은 "같은 값"에서만 요구한다. 툴의 Auto는 Catmull-Rom(중앙 차분, `EaseCurveBaker`와 같은 방식)으로 정의하고 문서에 적는다 |
| 그래프 성능 | 커브는 샘플 폴리라인(픽셀당 1점), 드래그 중 렌더만. VnTool 규칙 |
| VnTool 기능이 새어 들어온다(커맨드·타임라인 배치) | 툴은 Yarn·커맨드를 모른다 — `Mt.Authoring`에 그 타입이 없다 |
| 리그가 바뀌어 모션이 깨진다 | §4 해시·검증·소리 내기 |
| 두 생산자(Unity·툴)가 갈린다 | §6 바이트 동일 테스트를 양쪽 저장소에 둔다 |
