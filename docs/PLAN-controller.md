# 컨트롤러 — 애니메이터가 리그 계층을 몰라도 되게 (결정판, 2026-10-28)

마야에서 리거가 애니메이션용 컨트롤러를 만들듯, 이 툴도 리그의 세부 계층을 몰라도 다룰 수 있는 손잡이를 둔다.
근거는 무대 런타임 `Runtime/CharacterRig/Documentation~/rig-axis-order.md`(계층이 규약인 이유)와 커맨드가 어느 노드를
잡는지다. 소유자와 2026-10-28에 하나씩 정했다 — 아래 §0이 결정이고, 나머지는 그 결정의 풀이다.

---

## 0. 결정 (소유자, 2026-10-28)

| | 결정 | 뜻 |
|---|---|---|
| **쓰임** | 일상 연출은 커맨드(slide·fade·place·face·gesture…), 특별한 움직임만 `motion s1 <id>` | 툴은 **이름 붙은 재사용 클립**을 만든다. 수십만 자의 대본 사이에 반복해 쓰는 단순한 동작이 목적이고, 섬세한 연기는 목적이 아니다 |
| **D1 mode** | `motions/2` 그대로 — 커맨드 인자 하나. 한 모션에 absolute·relative가 섞여야 하면 두 클립·두 커맨드로 나눈다 | 종이연극이라 그럴 일이 드물다 |
| **D2 깊이** (고침) | level 하나를 **`CharSlot_DepthScale` 배율과 `CharSlot_DepthY` 위치 둘에** 굽는다. Y는 `−bust높이 × (배율 − 1)` | 처음엔 배율만 구웠는데(발 고정) 써 보니 발이 못 박힌 채 크기만 바뀌어 어색했다(소유자, 같은 날). 런타임 `size`의 focus 보존을 정지 자세에서 풀면 yCurve는 상쇄되고 이 식만 남는다(Mid = −820×0.708 = −580.56, 프리팹과 같다). bust 기본 높이(820)만 쓰므로 어느 캐릭터에도 같은 값이고, 얼굴은 제자리·발은 멀수록 위로 간다(사람 시야의 "먼 것은 위에"). 모션 뒤의 `size`가 둘을 다시 푼다. 시야각(원근 소실점) 개념은 보류 |
| **D3 슬롯 축** | 모션이 배치 커맨드와 같은 노드를 겹쳐 써도 된다. 스키마는 안 늘린다 | 뒤의 것이 앞의 것을 완주시키는 규약 위에서 연출가가 책임진다 |
| **D4 표의 집** | 리거의 지식 → 덤프 옆 `presets/controllers.json`(툴 내장본 포함). 익스포터는 손 안 댐 | |
| **Track_Focus** | place 커맨드(`PlaceCharacterFocusCommandCharR`)의 것. 모션은 건드리지 않는다(코어 규칙) | 화면 지점에 캐릭터의 지점을 정확히 두는 강제 커맨드 |
| **그룹 셋** | **자리**(placement, absolute) · **깊이**(depth, absolute) · **연기**(acting, relative) | "무엇에 대한 값인가"로 가른다 |
| **자리 X·Y** | `CharSlot_Track`(자유 이동, `move_by`의 자리) 하나 | `Track_X/Y`(`left/right`의 자리)는 숨긴다 |
| **연기 여섯** | 몸 · 기울기 · 숙임 · 떨림 · 크기 · 방향 | |

---

## 1. 계층의 역할 — 자리가 뜻이다

원칙(런타임 문서 §3): **한 노드 = 한 축**(동시에 돌 수 있는 것은 다른 노드), **위에 있는 것이 아래 전부에 곱해진다**
(원근은 `DepthScale`이 위에 있어서 생긴다), **bottom pivot 다섯 곳은 발이 땅에 붙어 있게 한다**.

### 1.1 슬롯 축 — 무대 배치 (`CharSlot_*`, 10개)

| 노드 | 뜻 | 주인(커맨드) | 컨트롤러 |
|---|---|---|---|
| `Track_Focus` | 얼굴(focus 점)이 화면 지점에 오도록 **푼 값**. 깊이보다 위라 화면 절대 좌표가 깨지지 않는다 | `place` 계열 | **금지**(코어 규칙) |
| `DepthY` | 깊이에 따른 Y 오프셋(발 위치) | `size`/깊이 프리셋 | **깊이**(배율에서 구운 Y, D2) |
| `DepthScale` | 깊이 배율. 아래 전부에 곱해져 이동량·몸짓 진폭까지 줄인다 = 원근 [bottom pivot] | `size`/깊이 프리셋 | **깊이** |
| `Track_Idle` | idle 흔들림 전용 자리(지금은 쓰지 않음) | — | ✗ |
| `Track` | 자유 이동(`move_by`)·등퇴장(`slide_in/out`) | 커맨드 | **자리 X·Y** |
| `Track_X` / `Track_Y` | `left/right` · `up/down`의 자리 | 커맨드 | ✗ |
| `Rotation` | 슬롯 회전(`Track` 아래라 이동 방향은 무대 기준) | (예약) | ✗ |
| `SwayPivot` | 발을 축으로 기울기(`rotate_by/reset`) [bottom pivot] | 커맨드 | **슬롯 기울기** |
| `Scale` | 슬롯 배율(`scale_by/reset`). 연기 축 전체에 곱해져 연기 진폭도 커진다 [bottom pivot] | 커맨드 | **자세 배율** |

### 1.2 배역 보정 축 (1개)

`CharacterPortrait_VisualOffset` — 스프라이트마다 다른 중심을 캐릭터별로 보정하는 자리(`set_anchor`·`show`). 연기 **위**라
연기가 보정된 자리를 기준으로 일어난다. 컨트롤러 아님.

### 1.3 초상 연기 축 — 모션의 본거지 (`CharacterPortrait_*`, 10개)

| 노드 | 뜻 | 주인 | 컨트롤러 |
|---|---|---|---|
| `Track` | 캐릭터별 기본 이동 보정(`ApplyTrackOffset`). 회전 위라 기울기와 무관 | 커맨드 | ✗ |
| `Rotation` | 몸 전체 기울기. `Track_Move` 위라 **연기 이동이 기울기를 따라간다** | — | **기울기** |
| `Track_Move` · `_X` · `_Y` | 연기 이동(한 축 트윈용으로 셋) | `show`가 리셋 | **몸 X·Y**(셋을 한 손잡이로) |
| `SwayPivot` | 발을 축으로 숙이기·젖히기(`char_rotate_to`) [bottom pivot] | 커맨드 | **숙임** |
| `Shake` | 진동 전용(`gesture`) | 커맨드 | **떨림 X·Y** |
| `ActingScale` · `_X` · `_Y` | 연기용 확대. 최하단이라 이동량은 안 바뀐다 | `char_scale_to`·`mirror`(`_X`) | **크기 X·Y** · **방향**(`_X`) |

### 1.4 스프라이트 (4개)

`Sprite_Root`(α, `fade`) · `Sprite_Image`(초상 PNG, `show`·`face`·`pose`) · `Overlay_Root/Image`(`face_swap`). 모션 규약 밖.

---

## 2. 세 컨트롤러 그룹

| 그룹 | 뜻 | 컨트롤러(노드) | 재생 mode | 값의 기준 |
|---|---|---|---|---|
| **자리** (placement) | 슬롯이 무대 어디에 있나 | 자리 X·Y(`CharSlot_Track`) · 슬롯 기울기(`CharSlot_SwayPivot` Z) · 자세 배율(`CharSlot_Scale` X·Y) | **absolute** | place가 잡은 자리 기준의 px·도·배. `DepthScale`이 위라 far에서는 같은 400이 화면에서 작게 간다 |
| **깊이** (depth) | 얼마나 멀리 있나 | 깊이 level 하나 → `CharSlot_DepthScale` X·Y 배율 + `CharSlot_DepthY` Y(−820×(배율−1))로 굽는다 | **absolute** | level 0(far) … 20(close), 배율은 `depth.json` 커브(mid 14 = 1.708 · Y −580.56) |
| **연기** (acting) | 그 자리에서 무엇을 하나 | 몸 X·Y · 기울기 · 숙임 · 떨림 X·Y · 크기 X·Y · 방향 | **relative**(휴지 자세가 항등이라 absolute여도 같다) | 슬롯 아래 로컬 — 슬롯이 (200,300)이면 몸 X −100은 화면 (100,300) |

### 2.1 종이연극의 동작 → 컨트롤러

| 동작 | 컨트롤러 |
|---|---|
| 끄덕(nod) · 젓기(shake head) | 몸 Y 작은 왕복 · 몸 X 작은 왕복 |
| 깡충(hop) | 몸 Y 위로 튀고 떨어짐 + 착지 크기 Y 스쿼시 |
| 움찔(flinch) | 몸 뒤로 + 기울기 뒤로 + 크기 살짝 줄임 |
| 떨림(tremble) | 떨림 X 고주파 |
| 숙임(bow) · 고개 젖힘 | 숙임 ± |
| 갸웃(lean) | 기울기 ± 작게 |
| 다가옴·물러남(step) | 자리 X(absolute) 또는 몸 X + 크기 |
| 바운스(bounce) · 들썩 | 크기 Y 왕복 |
| 호흡(breath) | 크기 Y 아주 작은 루프 |
| 돌아서기(turn) | 방향 1 → −1 (가운데 얇게) |
| 등장·퇴장 · 표정·포즈 · 화면 효과 | 커맨드의 몫(모션 규약 밖) |

### 2.2 툴에서의 모양

- **채널 박스**는 세 섹션(자리 회색 · 깊이 슬라이더 · 연기 파랑)이고 **오브젝트 하나를 통째로** 보인다. 노드 고르기가 없다.
  값 입력 = 지금의 길(Auto Key/키 있는 시각이면 키, 아니면 키 없는 포즈 → S). 깊이는 level → 배율을 구워 X·Y 둘에, 그 배율에서 Y를 구워 `DepthY`에 같이 쓴다(되돌리기 하나).
- **별칭**: 옛 모션(`nod`는 `Track_Move_Y`)이 "몸 Y"로 보인다. 표시는 주 노드 + 별칭의 합(배율은 곱)이고, 키는 **있는 채널**에,
  없으면 주 노드에 놓는다.
- **미리보기 mode**: 자리·깊이 채널이 하나라도 있는 모션을 relative로 두면 노란 경고("자리가 누적된다 — absolute로").
  돕는 손: "미리보기를 absolute로" · "두 모션으로 나누기"(자리·깊이 채널을 `<이름>_place`로 옮긴다).
- **하이어아키**는 오브젝트 밑에 `자리 › 깊이 › 연기` 폴더, 그 안이 컨트롤러. "리그 노드 보기"(설정)로 26단을 연다.
- **라벨**: 그래프·도프 시트·아웃라이너가 `연기 · 몸 Y`처럼 그룹을 앞에 붙인다. 표에 없는 채널은 노드 이름 그대로.
- **아웃라이너 = 모션의 컨트롤러 전부**(소유자, 2026-10-28): 표 순서 그대로(순서를 바꾸면 헷갈린다 — 고침). 고른 컨트롤러는 굵게, 키 없는 것은 흐리게. 컨트롤러를 고르면 그래프는 그 커브만, 키가 없으면 빈 그래프. 컨트롤러 밑은 표가 쓰는 성분만(B). 흐린 성분을 클릭해도 성분은 **생기지 않는다**(A2, 마야식) — 성분은 사용자가 키를 놓을 때만 생기고, 키 하나짜리 성분은 규칙 위반으로 보인다. 성분 행 × = 그 성분만 삭제. **S** = 키 없는 포즈가 있으면 그것을, 없으면 고른 컨트롤러의 지금 값을 현재 시각에 키.
- **매니퓰레이터**: W 몸 · E 숙임(발 축 고리) · R 크기. 자리 컨트롤러를 고른 채면 그 노드를 잡는다.
- **프리셋 모션**: 끄덕·젓기·깡충·움찔·떨림·숙임·바운스·호흡·돌아서기를 컨트롤러 위에 코드로 지어 "프리셋에서 추가".

---

## 3. 데이터 — `controllers/1`

내장본 `src/Mt.Authoring/Defaults/controllers.json`. 덤프 옆 `presets/controllers.json`(또는 `controllers.json`)이 있으면 그것이 이긴다.
규약(`motions/2`)·코어·런타임은 **안 바뀐다** — 컨트롤러는 툴의 표시·입력 층이다.

```json
{ "schema": "controllers/1",
  "rigs": [ { "rigKind": "character",
    "groups": [ { "id": "placement", "label": "자리", "mode": "absolute" },
                { "id": "depth",     "label": "깊이", "mode": "absolute" },
                { "id": "acting",    "label": "연기", "mode": "relative" } ],
    "controllers": [
      { "id": "place",    "group": "placement", "label": "자리",       "node": "CharSlot_Track",                  "kind": "position", "axes": ["x","y"], "unit": "px" },
      { "id": "slotLean", "group": "placement", "label": "슬롯 기울기", "node": "CharSlot_SwayPivot",              "kind": "euler",    "axes": ["z"],     "unit": "deg" },
      { "id": "stance",   "group": "placement", "label": "자세 배율",   "node": "CharSlot_Scale",                  "kind": "scale",    "axes": ["x","y"], "unit": "x" },
      { "id": "depth",    "group": "depth",     "label": "깊이",       "node": "CharSlot_DepthScale",             "kind": "scale",    "axes": ["x","y"], "unit": "level", "bake": "depthLevel", "bakeY": "CharSlot_DepthY" },
      { "id": "body",     "group": "acting",    "label": "몸",         "node": "CharacterPortrait_Track_Move",    "kind": "position", "axes": ["x","y"], "unit": "px",  "manipulator": true,
        "aliases": [ { "node": "CharacterPortrait_Track_Move_X", "axis": "x" }, { "node": "CharacterPortrait_Track_Move_Y", "axis": "y" } ] },
      { "id": "lean",     "group": "acting",    "label": "기울기",     "node": "CharacterPortrait_Rotation",      "kind": "euler",    "axes": ["z"],     "unit": "deg" },
      { "id": "bow",      "group": "acting",    "label": "숙임",       "node": "CharacterPortrait_SwayPivot",     "kind": "euler",    "axes": ["z"],     "unit": "deg", "manipulator": true },
      { "id": "shake",    "group": "acting",    "label": "떨림",       "node": "CharacterPortrait_Shake",         "kind": "position", "axes": ["x","y"], "unit": "px" },
      { "id": "squash",   "group": "acting",    "label": "크기",       "node": "CharacterPortrait_ActingScale",   "kind": "scale",    "axes": ["x","y"], "unit": "x",   "manipulator": true,
        "aliases": [ { "node": "CharacterPortrait_ActingScale_Y", "axis": "y" } ] },
      { "id": "facing",   "group": "acting",    "label": "방향",       "node": "CharacterPortrait_ActingScale_X", "kind": "scale",    "axes": ["x"],     "unit": "sign" }
    ] } ] }
```

| 필드 | 뜻 |
|---|---|
| `groups[].mode` | 이 묶음의 모션을 재생할 때 권장하는 mode(`relative`\|`absolute`). 툴의 경고와 미리보기 기본값이 쓴다 |
| `controllers[].node` `kind` `axes` | 키가 들어가는 노드·종류·성분. `kind`는 `position`\|`scale`\|`euler`, `z`는 `euler`에만(코어 규칙) |
| `aliases[]` | 같은 손잡이로 읽히는 (노드, 성분). 주 노드가 별칭을 이긴다(`facing`의 `ActingScale_X`가 `squash`의 별칭이 아니다) |
| `bake` | `depthLevel`: 값은 level, `depth.json` 커브로 배율을 구해 X·Y에 같이 쓴다(scale 노드에만) |
| `bakeY` | `depthLevel`과 함께: 배율에서 `−bust높이 × (배율 − 1)`을 구워 이 위치 노드의 Y에 쓴다(보통 `CharSlot_DepthY`). 없으면 배율만 |
| `manipulator` | W/E/R가 그 종류의 기본 손잡이로 고른다 |
| `unit` `note` | 화면 표기 |

표가 가리키는 노드가 리그에 없으면 상태 줄에 알린다(`ControllerRig.Validate`). 표가 없는 리그(`background`…)는 노드로 다룬다.
리그가 늘면 그 리그의 컨트롤러를 표에 더한다.

---

## 4. 단계

| 단계 | 무엇 | 끝 |
|---|---|---|
| **C1** 표·모델 ✅ | `controllers.json` 내장 + 덤프 옆 우선 · `ControllersFile`/`ControllerRig`(TryResolve·LabelOf·DefaultFor·Validate) · `DepthTuningFile.TryScaleOf/TryLevelOf` · 세션 | 테스트: 견본 `nod`(`Track_Move_Y`)가 "연기 · 몸 Y"로 풀린다, level↔배율 왕복 |
| **C2** 채널 박스 = 컨트롤러 ✅ | 세 섹션 · 값 입력 = 키 · 깊이 굽기 · 별칭 합산/있는 채널에 쓰기 · 경고와 돕는 손 · 표 없는 리그는 노드 박스 | 헤드리스: "숙임"에 15 → `CharacterPortrait_SwayPivot` euler Z 키 · 깊이 20 → `DepthScale` X·Y 2.2 |
| **C3** 하이어아키·라벨 ✅ | 오브젝트 › 자리/깊이/연기 폴더 › 컨트롤러 · "리그 노드 보기" 설정 · 도프/아웃라이너 라벨 | 라벨 `연기 · 몸 Y` |
| **C4** 매니퓰레이터 ✅ | W 몸 · E 숙임 · R 크기(표의 `manipulator`) · 자리 컨트롤러를 고른 채면 그 노드 · 표 밖 노드는 그대로 | 헤드리스: 자리 X·Y 고른 채 W → `CharSlot_Track`, E → `SwayPivot`, R → `ActingScale`, W → `Track_Move` |
| **C5** 프리셋 ✅ | 9종을 컨트롤러 위에 코드로(`MotionPresets`) · "프리셋에서 추가"(되돌리기 하나) | 전부 규칙·경고 없이 내보내기 · 끄덕 = 몸 Y −18 + 숙임 4° · 돌아서기 = 방향 1 → −1 commit |

C1–C5 전부 `dev`에 들어갔다(2026-10-28, CI 녹색). 남은 것은 쓰면서 나온다 — 후보: 프리셋의 진폭·타이밍 손보기(애니메이터 피드백), 리그 노드 보기에서 컨트롤러 노드 표시, `controllers.json`을 리거가 고칠 때의 검증 메시지(`Validate`) 표면화, 자리·깊이 컨트롤러의 `place`·`size` 커맨드와의 왕복(툴에서 커맨드로 내보내기).
