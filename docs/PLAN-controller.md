# 컨트롤러 — 애니메이터가 리그 계층을 몰라도 되게 (조사와 계획, 2026-10-28)

소유자 요청: 마야에서 리거가 리깅만 하지 않고 **애니메이션용 컨트롤러**를 만들듯, 이 툴도 리그의
세부 계층과 상관관계를 몰라도 다룰 수 있는 손잡이가 필요하다. 지금은 26단 계층을 해박하게
알아야 손을 댈 수 있다. 먼저 각 계층의 역할을 정리하고, 그다음 종이연극(트릭컬·블루 아카이브·
스텔라 소라의 다이얼로그) 연출에 필요한 컨트롤러가 무엇인지 알아본다.

근거: 무대 런타임 `Runtime/CharacterRig/Documentation~/rig-axis-order.md`(계층이 규약인 이유),
`CharacterRigDefinition.cs`(스키마), 커맨드가 어느 노드를 잡는지(grep), `rig-schemas.json`(기본값 — 전부 항등).

---

## 1. 계층의 역할 — 자리가 뜻이다

원칙(런타임 문서 §3): **한 노드 = 한 축**(동시에 돌 수 있는 것은 다른 노드), **위에 있는 것이 아래
전부에 곱해진다**(원근은 `DepthScale`이 위에 있어서 생긴다), **bottom pivot 다섯 곳은 발이 땅에
붙어 있게 한다**. 표의 "주인"은 그 노드에 값을 쓰는 쪽이고, "모션"은 이 툴이 키를 놓아도 되는가다.

### 1.1 슬롯 축 — 무대 배치 (`CharSlot_*`, 10개)

| 노드 | 뜻 | 주인(커맨드) | 모션 |
|---|---|---|---|
| `Track_Focus` | 얼굴(focus 점)이 화면 지점에 오도록 **푼 값**. 깊이보다 위라 화면 절대 좌표가 깨지지 않는다 | `place` 계열(해(解)) | **금지**(규칙) |
| `DepthY` | 깊이에 따른 Y 오프셋(발 위치) | `size`/깊이 프리셋 | ✗ — 씬의 층(layer) |
| `DepthScale` | 깊이 배율. 아래 전부에 곱해져 **이동량·몸짓 진폭까지** 줄인다 = 원근 [bottom pivot] | `size`/깊이 프리셋 | ✗ — 씬의 층 |
| `Track_Idle` | idle 흔들림 전용 자리(깊이에 따라 줄고, `move_by`와 안 싸운다) | (idle 시스템 — 지금은 쓰지 않음) | △ 호흡 같은 "늘 도는" 모션 자리 후보 |
| `Track` | 자유 이동(`move_by`)·등퇴장(`slide_in/out`) | 커맨드 | △ 씬 "오프셋 X/Y" |
| `Track_X` / `Track_Y` | `left/right` · `up/down` — 좌우·상하가 다른 트윈이라 노드가 둘 | 커맨드 | ✗ |
| `Rotation` | 슬롯 회전. `Track` **아래**라 회전해도 이동 방향은 무대 기준 | (예약) | ✗ |
| `SwayPivot` | 발을 축으로 기울기(`rotate_by/reset`) [bottom pivot] | 커맨드 | △ 씬 "Rot Z" |
| `Scale` | 슬롯 배율(`scale_by/reset`). 연기 축 전체에 곱해져 **연기 진폭도 커진다** [bottom pivot] | 커맨드 | △ 씬 "Scale X/Y" |

→ 이 축은 **대본의 배치 커맨드와 씬이 쓰는 자리**다. 모션이 여기에 키를 놓으면 틀리지는 않지만
(한 노드 = 한 축이라 뒤의 것이 앞의 것을 완주시킨다) 배치와 싸운다. 컨트롤러에서는 **숨긴다**
("고급"으로만 연다).

### 1.2 배역 보정 축 (1개)

| 노드 | 뜻 | 주인 | 모션 |
|---|---|---|---|
| `CharacterPortrait_VisualOffset` | 스프라이트마다 그려진 중심이 달라서 넣는 캐릭터별 보정. 연기 **위**라 연기가 보정된 자리를 기준으로 일어난다 [bottom pivot] | `set_anchor` · `show`(배역 앵커 표) | ✗ |

### 1.3 초상 연기 축 — 모션의 본거지 (`CharacterPortrait_*`, 10개)

| 노드 | 뜻 | 주인 | 모션 |
|---|---|---|---|
| `Track` | 캐릭터별 기본 이동 보정(`ApplyTrackOffset`). 회전 **위**라 기울기와 무관 | 커맨드 | ✗ |
| `Rotation` | 몸 전체 기울기. `Track_Move` **위**라 **연기 이동이 기울기를 따라간다**(몸이 기울면 흔들림도 기운다) | — | **○ 기울기** |
| `Track_Move` | 연기 이동의 부모(자유 이동) | `show`가 리셋 | **○ 몸(이동)** |
| `Track_Move_X` / `_Y` | 축별 연기 이동(한 축 트윈용) | 견본 `nod`가 `_Y` | ○ (몸과 같은 뜻 — 컨트롤러는 하나로 묶는다) |
| `SwayPivot` | 발을 축으로 숙이기·젖히기(`char_rotate_to`) [bottom pivot] | 커맨드 | **○ 숙임** |
| `Shake` | 진동 전용(`gesture`의 사인 진동) | 커맨드 | **○ 떨림** |
| `ActingScale` | 연기용 확대. **최하단**이라 이동량은 안 바뀐다(스프라이트만) | `char_scale_to` | **○ 크기(스쿼시·스트레치)** |
| `ActingScale_X` | `mirror`가 −1로 뒤집는 자리 | `mirror` | **○ 방향**(−1/1) |
| `ActingScale_Y` | 축별 확대 | — | ○ (크기와 묶는다) |

### 1.4 스프라이트 (4개)

| 노드 | 뜻 | 주인 | 모션 |
|---|---|---|---|
| `Sprite_Root` | 가시성(CanvasGroup α, 초기 0) | `fade_in/out` · `show` | ✗ — 모션 규약은 변환뿐(α 없음) |
| `Sprite_Image` | 초상 PNG·사이징(`HeightFitPreserveAspect`) | `show` · `face` · `pose` | ✗ |
| `Overlay_Root/Image` | `face_swap`의 겹침 페이드 층 | 커맨드 | ✗ |

### 1.5 한 줄 요약

애니메이터가 만질 자리는 **1.3의 여섯 뜻**(기울기 · 몸 · 숙임 · 떨림 · 크기 · 방향)뿐이다.
나머지 열여섯 노드는 배치·배역·가시성의 자리라 **대본과 씬이 주인**이다. 지금 툴은 26개를 같은 무게로
보여 주므로 "어디를 만져야 하는가"가 안 보인다 — 그것이 컨트롤러가 풀 문제다.

---

## 2. 종이연극 연출 — 필요한 동작과 컨트롤러

### 2.1 종이연극의 동작 어휘

트릭컬·블루 아카이브·스텔라 소라류 다이얼로그는 한 장 초상을 **발을 축으로** 움직이는 종이연극이다.
스프라이트 자체는 안 변하고(표정·포즈는 교체), 변환 몇 가지의 조합이 연기 전부다. 자주 쓰는 것:

| 동작 | 뜻 | 변환 |
|---|---|---|
| 끄덕(nod) · 젓기(shake head) | 긍정·부정 | 몸 Y 작은 왕복 · 몸 X 작은 왕복 |
| 깡충(hop) · 두 번 깡충 | 기쁨·놀람 | 몸 Y 위로 튀고 떨어짐(착지 스쿼시) |
| 움찔(flinch) · 뒤로 물러섬 | 놀람·겁 | 몸 뒤로 + 기울기 뒤로 + 크기 살짝 줄임 |
| 떨림(tremble) · 분노 떨림 | 두려움·분노 | 떨림 X 고주파 |
| 숙임(bow) · 고개 젖힘 | 인사·사과·웃음 | 숙임(발 축) ± |
| 기울기(lean) · 갸웃 | 의문·관심 | 기울기 ± 작게 |
| 다가옴(step in) · 물러남(step out) | 강조·거리 | 몸 X 이동 + 크기 살짝(연기 확대) |
| 바운스(bounce) · 들썩 | 즐거움·웃음 | 크기 Y 스쿼시·스트레치 왕복 |
| 호흡(breath, idle) | 살아 있음 | 크기 Y 아주 작은 루프(또는 `Track_Idle`) |
| 돌아서기(turn) | 방향 전환 | 방향 −1 ↔ 1 (크기 X로 뒤집기, 가운데 잠깐 얇게) |
| 등장·퇴장 | 입퇴장 | 슬라이드(`slide_in/out`) · 페이드 — **커맨드의 몫** |
| 표정·포즈 교체 · 이모지 · 화면 흔들림 · 플래시 | — | **모션 규약 밖**(스프라이트·화면 효과·카메라) |

### 2.2 컨트롤러 여섯 — 이름 · 자리 · 단위

컨트롤러는 **이름 → (노드, 종류, 성분) 고정 대응**이다. 키는 지금처럼 노드 채널에 저장된다(`motions/2`
그대로 — 규약은 안 바뀐다). 애니메이터는 노드 이름을 보지 않는다.

| 컨트롤러 | 속성 | 대응 | 단위 · 기본 | 뜻 |
|---|---|---|---|---|
| **몸** (Body) | X · Y | `CharacterPortrait_Track_Move` position | px · 0 | 연기 이동. 기울기를 따라간다 |
| **기울기** (Lean) | 각 | `CharacterPortrait_Rotation` euler Z | 도 · 0 | 몸 전체 기울기(가운데 축) |
| **숙임** (Bow) | 각 | `CharacterPortrait_SwayPivot` euler Z | 도 · 0 | 발을 축으로 숙임·젖힘 |
| **떨림** (Shake) | X · Y | `CharacterPortrait_Shake` position | px · 0 | 고주파 진동 전용 |
| **크기** (Squash) | X · Y | `CharacterPortrait_ActingScale` scale | 배 · 1 | 스쿼시·스트레치·연기 확대(이동량 불변) |
| **방향** (Facing) | 좌/우 | `CharacterPortrait_ActingScale_X` scale | −1/1 · 1 | 뒤집기(`mirror`와 같은 자리) |

고급(접힘): **자세**(`CharSlot_Scale` — 이동 진폭까지 커지는 "전체 배율"), **슬롯 기울기**(`CharSlot_SwayPivot`),
**자리**(`CharSlot_Track`). 이것들은 씬 배치와 같은 자리라 "커맨드와 싸운다"는 표시와 함께 연다.

`Track_Move_X/_Y`는 몸의 X/Y로 접는다 — 한 축만 키잉하면 그 성분만 생기므로 "한 노드 = 한 축" 규약과
충돌하지 않는다(견본 `nod`는 `_Y`에 있다 — 컨트롤러 표가 `Track_Move`와 `Track_Move_X/_Y`를 같은 손잡이로
읽게 하면 옛 모션도 그대로 보인다).

### 2.3 컨트롤러가 가져다주는 것

- **채널 박스가 여섯 줄이 된다**: 몸 X/Y · 기울기 · 숙임 · 떨림 X/Y · 크기 X/Y · 방향. 지금의 7줄
  Translate/Rotate/Scale(노드 하나)과 달리 **캐릭터 하나를 통째로** 보인다. 노드 고르기가 사라진다.
- **하이어아키는 컨트롤러 목록**이 기본이고, "리그 노드 보기"로 26단을 연다(리거·디버그용).
- **그래프·도프 시트 라벨**이 "몸 Y", "숙임"이 된다. 파일은 그대로 노드 채널이다.
- **매니퓰레이터**: W = 몸, E = 숙임(발 축 고리), R = 크기. 기울기·떨림·방향은 채널 박스로.
- **프리셋 모션**(끄덕·젓기·깡충·움찔·떨림·숙임·바운스·호흡·돌아서기)을 툴로 만들어 저작본으로 싣는다.
  "모션 추가 › 프리셋에서"로 복제해 고친다. 컨트롤러 위에 만들었으므로 어느 캐릭터에도 맞는다.

---

## 3. 데이터 — 컨트롤러 표는 어디에 사는가

컨트롤러는 **리그의 지식**이다(리거가 만드는 것). 그래서 리그 덤프 옆에 둔다:
`ExportedTuning/presets/controllers.json`(또는 `rig-schemas.json`의 `controllers[]`). 툴은 내장본을 갖고,
덤프 옆에 있으면 그것을 쓴다.

```json
{ "schema": "controllers/1",
  "rigs": [ { "rigKind": "character",
    "controllers": [
      { "id": "body",   "label": "몸",     "node": "CharacterPortrait_Track_Move",  "kind": "position", "axes": ["x","y"], "unit": "px",  "aliases": ["CharacterPortrait_Track_Move_X","CharacterPortrait_Track_Move_Y"] },
      { "id": "lean",   "label": "기울기", "node": "CharacterPortrait_Rotation",    "kind": "euler",    "axes": ["z"],     "unit": "deg" },
      { "id": "bow",    "label": "숙임",   "node": "CharacterPortrait_SwayPivot",   "kind": "euler",    "axes": ["z"],     "unit": "deg" },
      { "id": "shake",  "label": "떨림",   "node": "CharacterPortrait_Shake",       "kind": "position", "axes": ["x","y"], "unit": "px" },
      { "id": "squash", "label": "크기",   "node": "CharacterPortrait_ActingScale", "kind": "scale",    "axes": ["x","y"], "unit": "x",
        "aliases": ["CharacterPortrait_ActingScale_X","CharacterPortrait_ActingScale_Y"] },
      { "id": "facing", "label": "방향",   "node": "CharacterPortrait_ActingScale_X","kind": "scale",   "axes": ["x"],     "unit": "sign", "advanced": false },
      { "id": "stance", "label": "자세(전체 배율)", "node": "CharSlot_Scale", "kind": "scale", "axes": ["x","y"], "unit": "x", "advanced": true, "warn": "씬 배치·scale_by와 같은 자리" }
    ] } ] }
```

- 규약(`motions/2`)·코어·런타임은 **안 바뀐다**. 컨트롤러는 툴의 표시·입력 층이다.
- 표에 없는 노드에 키가 있는 모션(옛 것·유니티에서 만든 것)은 "리그 노드" 보기에서 그대로 보인다.
- 리그가 늘면(`rigKind` 추가) 표에 그 리그의 컨트롤러를 더한다 — 배경 리그라면 `Background_Shake`·`Background_ActingScale` 등.

---

## 4. 단계(안)

| 단계 | 무엇 | 끝 |
|---|---|---|
| **C1** 컨트롤러 표 | `controllers.json` 내장 + 덤프 옆 우선 · 모델 `ControllerSet`(id→노드/종류/성분, aliases) · 채널→컨트롤러 역대응 | 테스트: 견본 `nod`(`Track_Move_Y`)가 "몸 Y"로 풀린다 |
| **C2** 채널 박스 = 컨트롤러 | 선택 오브젝트의 컨트롤러 전부를 한 번에(노드 고르기 없음) · 값 입력 = 키(지금 길 그대로) · 고급 접기 | 헤드리스: "숙임"에 15 → `CharacterPortrait_SwayPivot` euler Z 키 |
| **C3** 하이어아키·라벨 | 오브젝트 밑에 컨트롤러 목록(기본) · "리그 노드 보기" 토글 · 그래프/도프/아웃라이너 라벨 | 라벨 "몸 · y" |
| **C4** 매니퓰레이터 재대응 | W 몸 · E 숙임(발 축) · R 크기 · 방향은 버튼 | 뷰포트 테스트 갱신 |
| **C5** 프리셋 모션 | 끄덕·젓기·깡충·움찔·떨림·숙임·바운스·호흡·돌아서기 저작본 내장 · "프리셋에서 추가" | 전부 규칙 통과 · 내보내기 바이트 고정 |

소유자가 정할 것: ① 컨트롤러 이름(한글 라벨·영문 id — 위 표는 안), ② 슬롯 축(자세·슬롯 기울기·자리)을
고급으로라도 열지, ③ 컨트롤러 표의 집(덤프 옆 `presets/controllers.json` vs `rig-schemas.json` 안 —
후자는 익스포터 변경), ④ α(페이드)·표정 교체를 모션에 넣을지(규약 확장 — 별도 결정).
