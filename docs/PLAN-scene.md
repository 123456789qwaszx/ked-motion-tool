# 씬·오브젝트·월드 축·리그 선택 — 조사와 제작 계획

소유자 요청(2026-10-28): 지금은 하이어아키도 없고 씬에 캐릭터 하나만 선다. **Scene과 그 안의 오브젝트들**이 있어야 하고, 그러려면 **월드 축**이 필요하며, 모션 가져오기/내보내기도 "고정된 리그 하나"가 아니라 **어떤 리그의 모션인지**를 다루는 설계가 필요하다. 그것을 조사하고 계획을 세운다.

기준 그림: 마야 채널 박스(Translate/Rotate/Scale/Rotate Order), Unity 저작 씬의 하이어아키(`[MotionAuthoring] › Stage › CharacterRig › CharSlot_* › CharacterPortrait_* › Sprite`), 마야 뷰포트의 월드 격자.

---

## 1. 조사 — 지금 무엇이 있고 무엇이 묶여 있나

### 1.1 코어(반입본)는 이미 "여러 캐릭터가 선 무대"를 안다

| 코어 개념 | 뜻 | 툴이 쓰는가 |
|---|---|---|
| `StageState` | 무대 확정 상태: 루트 공간(기준 해상도) + 노드 트리 + **슬롯** + 슬롯 부착(**stage00~02 × layer far/back/mid/front/close**) + 샷 의도(zoom·pan·focus) + 배역/별칭 | 안 쓴다. 뷰포트는 `RectNodeTree` 하나를 직접 세운다 |
| 슬롯(`slot c1 stage01 mid`) | 리그 인스턴스 하나. 노드 키는 `c1/CharSlot_Track`처럼 **슬롯 접두사 + 노드 id**. `RigSchemaLoader.AddRigTo(tree, rig, keyPrefix)`가 같은 리그를 여러 번 세운다 | 안 쓴다(접두사 없는 트리 하나) |
| `rig-schemas.json` | `rigs[]` — **rigKind 여러 개**를 담는 파일. 각 리그는 `__root` 아래 노드 목록(부모·앵커·피벗·크기·스케일·오일러·CanvasGroup) | `RigSchemaFile.TryGetRig(rigKind)`·`NodeIds`(rigKind → 노드 집합)로 **이미 리그별**로 다룬다 |
| 리그 덤프의 노드 구성 | `CharSlot_*` 사슬 = **배치 축**(Track_Focus·DepthY·DepthScale·Track_Idle·Track·Track_X/Y·Rotation·SwayPivot·Scale — place/size/depth/nudge/move가 겨눈다) · `CharacterPortrait_*` 사슬 = **연기 축**(Track_Move·SwayPivot·Shake·ActingScale — 모션이 겨눈다) · `*Sprite_Root/Image` = 그림 | 전부 한 트리로 세우되 mid 고정·배치 축은 손대지 않는다 |
| 깊이 | layer 라벨 → `presets/depth.json`의 depthY·depthScale → `CharSlot_DepthY`·`CharSlot_DepthScale` | mid(DepthScale 1) 고정 |
| 샷(카메라) | `ShotIntentState`(zoom·pan·focus) · `ShotIntentMath.EvaluateCameraScale` | 없다 |
| 좌표계 | 루트 공간 = 기준 해상도(`base-resolution.json`, 1920×1080), **가운데 원점·y 위**, 단위 px. `1u` = 기준 폭/48 = 40px(`UnitToken`) | 뷰포트가 같은 공간에 그린다(격자·축 표시는 없다) |

### 1.2 모션 규약은 처음부터 "리그 종류에 묶인 데이터"다

- `motions/1`의 클립은 `name · rig(rigKind) · duration · end · channels[node id]`. **노드 id에 슬롯 접두사는 없다** — 모션은 특정 캐릭터가 아니라 **리그 종류**에 묶인다. 런타임은 `motion(slot, name)`으로 어느 슬롯에 입힐지 그때 정한다.
- 툴의 저작본 `MotionAsset.Rig`도 rigKind다. 검증(`MotionValidator`)은 `rigKind → 노드 집합` 사전으로 **리그별로** 노드 존재를 본다. 뷰포트도 `motion.Rig`로 템플릿을 세운다.
- 즉 "가져오기가 고정된 리그 하나를 타겟"으로 보이는 것은 **UI가 오브젝트 하나만 세우기 때문**이지 파일·검증·수학의 한계가 아니다. 덤프에 rigKind가 둘 이상 있으면 지금도 그 리그의 모션을 열고 검증하고 내보낸다 — 다만 고를 자리가 없다.

### 1.3 런타임 쪽 한계 (이 저장소 밖)

- 리듀서의 `slot` 폴드는 **`rigKind == "character"`만** 세운다(`TryFindCharacterRig`). 다른 rigKind(소품·배경·이모지…)의 슬롯을 세우려면 무대 런타임에 "어떤 리그를 세울지" 인자가 필요하다. 툴은 그 변화를 **기다리지 않고** 설계하되, 내보낸 모션이 재생되려면 런타임이 그 리그 슬롯을 세울 수 있어야 한다는 점을 문서에 못 박는다.
- 런타임 씬은 Yarn 커맨드 열을 접은 결과다. 툴의 "씬"은 **연출을 만들기 위한 미리보기 배치**이지 런타임에 내보내는 산출물이 아니다(연출 클립 규약은 `PLAN.md` §8, 아직 없다).

### 1.4 묶여 있는 곳 (고칠 곳)

| 자리 | 지금 | 묶인 이유 |
|---|---|---|
| `ViewportView` | 템플릿 트리 하나 + 초상 하나 + 매니퓰레이터 하나 | 오브젝트 개념이 없다 |
| `DocumentSession` | 리그 덤프 1 · 초상 경로 1 · 무대 크기 | 씬 데이터가 없다 |
| `MotionDocument.Preview` | `Portrait` 하나 | 미리보기 = 캐릭터 하나 |
| 왼쪽 패널 | 모션 목록 · 채널 추가(노드 콤보) | 노드가 "어느 오브젝트의"인지 묻지 않는다 |
| 모션 추가 폼 | rig 콤보 | 있다 — 다만 선택된 오브젝트와 무관 |

---

## 2. 설계 결정(제안)

### 2.1 씬 = 코어 `StageState`의 저작본

툴이 자기만의 씬 그래프를 새로 짓지 않는다. **씬 = 슬롯(오브젝트) 목록 + 각 슬롯의 부착(stage·layer)과 배치 값**이고, 그리기는 코어가 세우는 트리(`AddRigTo` + 슬롯 접두사)로 한다. 이유:

1. 보이는 배치 = 런타임이 접는 배치. 리듀서와 **같은 함수**로 세우므로 "툴에서는 맞는데 게임에서 다르다"가 안 생긴다(`PLAN.md` §1 미리보기 원칙의 연장).
2. 노드 키 규약(`c1/CharacterPortrait_Track_Move_Y`)이 이미 있다. 모션은 접두사 없는 id, 오브젝트는 접두사 — 합성은 `StageState.NodeKeyOf`.
3. 나중에 연출 클립(여러 오브젝트 타임라인)이 생겨도 같은 상태 위에 선다.

```
SceneAsset                        // 저작본 *.motion-authoring.json 안 "scenes": [...] (미리보기 전용, 내보내지 않는다)
  name
  stage: 기준 해상도(base-resolution) · 격자 설정
  camera: zoom · pan (ShotIntentState 값, 보기 전용)
  objects[]: SceneObject
    id          "c1"            // 슬롯 키. 노드 접두사
    rig         "character"     // rigKind — rig-schemas.json의 리그
    stage/layer "stage01"/"mid" // 부착. layer가 깊이(depth 프리셋)를 정한다
    placement   { CharSlot_Track_X.position, CharSlot_Track_Y.position, CharSlot_Rotation.euler, CharSlot_Scale.scale, … }
                                // 배치 축 노드의 값 — 마야 채널 박스의 Translate/Rotate/Scale이 여기
    portrait    { image 경로, variant, emotion }  // 그림(미리보기)
    visible     bool            // CanvasGroup alpha
    motionPreview "nod" | null  // 이 오브젝트에 입혀 볼 모션(저장 안 함도 가능)
```

### 2.2 월드 축·격자

- 루트 공간은 이미 "가운데 원점·y 위·px"다. 뷰포트에 **원점 마커, x(빨강)·y(초록) 축, 격자**(가는 선 1u = 40px — 런타임의 `1u`와 같은 눈금, 굵은 선 5u), **무대 프레임**(기준 해상도)을 그린다. 마야 격자와 같은 자리다.
- 깊이(layer)는 축이 아니라 **프리셋**이다(depthY·depthScale). 하이어아키에서 레이어로 보이고, 뷰포트는 앞 레이어를 나중에 그린다. z축 격자는 두지 않는다(규약에 z 배치가 없다).
- 카메라(샷)는 **보기 전용**으로 먼저 둔다: zoom·pan을 바꾸면 `ShotIntentMath`로 뷰포트 전체가 움직인다. 카메라 키는 규약이 없으므로 넣지 않는다(`PLAN.md` §8 카메라 채널이 생기면 그때).

### 2.3 하이어아키와 채널 박스

```
Scene "scene_a"
 ├ stage01
 │  ├ mid
 │  │  ├ c1 : character   ← 오브젝트(슬롯). 클릭 = 선택, 뷰포트 매니퓰레이터 대상
 │  │  │   └ (리그 노드 — 접어 둔다; 펼치면 CharSlot_… › CharacterPortrait_… 트리)
 │  │  └ c2 : character
 │  └ front
 └ camera
```

- 오브젝트를 고르면 오른쪽에 **채널 박스**: Translate X/Y · Rotate Z · Scale X/Y · Layer(깊이) · Visible · Portrait(image/variant/emotion). 값을 바꾸면 **배치**(씬)가 바뀐다 — 모션 키가 아니다.
- 리그 노드를 펼쳐 고르면 그 노드가 채널 추가의 대상이 된다(지금의 노드 콤보를 대신한다). 연기 축 노드가 기본으로 보이고, 배치 축은 "고급"으로 접는다(런타임의 place/size와 충돌할 수 있어서 — 규약은 막지 않지만 툴이 경고한다).

### 2.4 리그 선택·모션 바인딩·내보내기

- **모션의 주인은 rigKind다**(규약 그대로). 오브젝트는 "이 모션을 어디에 입혀 볼까"일 뿐이다.
- 모션 목록은 rigKind로 묶어 보인다. 새 모션은 **선택한 오브젝트의 rigKind**로 만든다. 모션을 고르면 미리보기 대상은 같은 rigKind의 오브젝트 중 선택된 것(없으면 첫 것).
- 채널 추가·검증·아웃라이너는 그 rigKind의 노드만 보인다(지금의 `RigNodeIds` 그대로).
- **내보내기**: `motions.json`은 지금처럼 모든 모션(여러 rigKind 섞임) — 런타임은 이름으로 찾고 `rig`로 맞춘다. 필터를 둔다: 전체 / 이 rigKind만 / 이 오브젝트가 입는 모션만. 파일 안에 rigKind가 섞이는 것은 규약이 허용한다(검증은 리그별).
- **가져오기**: 클립의 `rig`가 덤프에 없으면 "리그 덤프에 rigKind 'x'가 없다"를 **지금처럼** 알린다(이미 있는 검증). 덤프에 리그를 더하는 것은 Unity 익스포터의 몫이다.

### 2.5 저장

씬은 저작본 파일 안 `scenes[]`에 든다(`motion-authoring/1`에 선택 항목으로 더한다 — 없으면 지금처럼 캐릭터 하나 기본 씬). 산출물 `motions/1`에는 아무것도 더하지 않는다.

---

## 3. 단계

| 단계 | 내용 | 끝났다는 증거 |
|---|---|---|
| **S0** 결정 | 이 문서를 소유자가 확인. 특히 ⓐ 씬은 미리보기 전용(내보내지 않음) ⓑ 모션의 주인은 rigKind ⓒ 비-character 리그의 재생은 런타임 `slot` 확장이 선행 | 확인 |
| **S1** 씬 모델·월드 축 | `SceneAsset/SceneObject` 모델 + `MotionEditor` 명령(오브젝트 추가/삭제/배치/레이어/보이기) · 저작본 왕복 · 뷰포트: 격자·축·무대 프레임, 여러 오브젝트를 코어 트리로 세워 그리기(슬롯 접두사), 선택 오브젝트 매니퓰레이터 | 두 캐릭터를 다른 레이어에 세우고 저장→열기 왕복 테스트. 배치 = 리듀서 `place`와 같은 좌표(코어 함수로 대조) |
| **S2** 하이어아키·채널 박스 | 왼쪽 하이어아키(Scene › stage › layer › object › 리그 노드), 채널 박스(Translate/Rotate/Scale/Layer/Visible/Portrait), 노드 선택 → 채널 추가 | 헤드리스: 오브젝트 선택·값 입력·노드로 채널 추가 |
| **S3** 리그 선택·바인딩·내보내기 | 모션 목록 rigKind 묶음 · 새 모션 = 선택 오브젝트의 rigKind · 미리보기 대상 선택 · 내보내기 필터 · 덤프에 리그 둘 이상인 픽스처 | 두 rigKind 픽스처로 가져오기·검증·필터 내보내기 바이트 테스트 |
| **S4** 깊이·카메라 보기 | layer → `depth.json` 프리셋(DepthY·DepthScale) 적용 · 카메라 zoom/pan 보기(`ShotIntentMath`) · focus 프리셋 표시(선택) | 깊이 좌표가 코어 `SetDepth`와 같다는 테스트 |
| **S5** 정리 | README·`PLAN.md` §8 갱신, `maya-comparison.md`에 씬 절 | 문서 |

크기: S1이 가장 크다(뷰포트·모델·명령). S2는 UI 조립. S3·S4는 작다(데이터는 이미 리그별이다).

---

## 4. 규약·런타임에 요청할 것 (이 저장소 밖, 소유자가 정한다)

1. **`slot` 폴드의 리그 선택** — 지금은 `character` 고정. `slot c1 stage01 mid [rigKind]`처럼 리그를 받거나, rigKind별 슬롯 커맨드가 필요하다. 툴은 그 전에도 여러 rigKind의 모션을 만들 수 있지만 재생은 못 한다.
2. **리그 덤프에 리그 추가** — 소품·배경 등 새 `rigKind`는 Unity 익스포터가 덤프에 더한다. 툴은 덤프만 본다(`PLAN.md` §4.4).
3. **카메라·깊이 채널**은 규약 밖이다. 툴은 보기 전용으로 둔다. 필요해지면 `PLAN.md` §8의 순서대로.

---

## 5. 하지 않는 것

- 3D 격자·원근 카메라(마야 persp) — 무대는 2D 캔버스다. 격자는 px 평면이다.
- 씬을 런타임 산출물로 내보내기 — 런타임 씬은 Yarn이 만든다.
- 여러 오브젝트의 시간차 연출(연출 클립) — 별 규약이 먼저다(`PLAN.md` §8).

## 결정 (2026-10-28, 소유자)

- 모션의 주인은 **rigKind**. 공통 스키마를 가진 리그끼리는 모션을 공유할 수 있다 → 툴은 "rigKind가 같거나 채널 노드가 전부 있으면" 그 오브젝트에 입혀 보인다(`ScenePreview.CanWear`).
- 씬은 **미리보기 전용**. 유니티 런타임에 보내지 않는다.
- 비-character 리그 재생은 **당분간 고려하지 않는다**. 캐릭터 리그부터 제대로 한다.

## 상태

- **S1 완료**(`dev`): `SceneAsset/SceneObject` · 편집기 씬 명령(추가/삭제/배치/레이어/보이기/초상, 각각 되돌리기 하나) · 저작본 `scenes[]` 왕복과 예전 `preview.portrait` 이관 · `ScenePreview`(코어 `AddRigTo`로 접두사 트리, 배치 축 노드, 초상 사이징, 선택 오브젝트에만 모션) · 뷰포트 격자/축/프레임 · 오브젝트 클릭 선택 · 왼쪽 오브젝트 목록 · 오른쪽 채널 박스(X/Y/Rot/Scale/Layer/보이기).
- 다음: S2 하이어아키 트리(Scene › stage › layer › object › 리그 노드)·노드 선택으로 채널 추가, S3 모션 목록 rigKind 묶음·내보내기 필터, S4 깊이 프리셋·카메라 보기.
