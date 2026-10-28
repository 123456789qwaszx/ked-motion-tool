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

---

## 6. S6 — 리그 노드의 실제 계층 · 유니티식 오브젝트 계층 (소유자 요청, 2026-10-28)

요청: ⓐ 캐릭터 리그의 하이어아키는 "연기 축/배치 축" 폴더가 아니라 **유니티에 실제로 정리된 부모–자식 계층 그대로** 보여야 한다(코어가 이미 그렇게 안다). ⓑ 유니티 하이어아키처럼 **우클릭 메뉴로 오브젝트를 편하게 더하며 계층을 잡고** 오브젝트 하나하나를 조작하고 싶다.

### 6.1 조사

- 덤프 `rig-schemas.json`의 노드마다 `parent`가 있고 "부모가 항상 자식보다 먼저"다. 코어 `RigSchemaLoader.AddRigTo`가 그 순서대로 `RectNodeTree.Add(key, parentKey, …)`를 부르고, `RectNodeTree.GetParentKey`로 되짚을 수 있다. 캐릭터 리그는 `__root › CharSlot_Track_Focus › CharSlot_DepthY › … › CharSlot_Scale › CharacterPortrait_VisualOffset › … › CharacterPortrait_ActingScale_Y › CharacterPortraitSprite_Root › CharacterPortraitSprite_Image`(+ `…Overlay_Root/Image`) 한 사슬이다. 툴이 `IsPlacementAxis`로 둘로 나눠 접은 것은 **툴의 임의 분류**였다 — 뺀다.
- `AddRigTo(tree, rig, keyPrefix, attachParentKey)`는 리그를 **이미 있는 노드 밑에** 붙일 수 있다. 즉 코어는 "오브젝트 밑의 오브젝트"(소품이 캐릭터 손에, 오브젝트가 배경의 `Background_ObjectSlotRoot`에)를 이미 지원한다. 부모 노드가 움직이면 자식 리그는 코어 체인으로 따라온다 — 툴이 좌표를 더하지 않는다.
- 유니티 "Create Empty"에 해당하는 것: 노드 하나짜리 오브젝트. 코어 `RectNodeTree.Add`에 빈 `RectNodeState`(가운데 앵커·피벗, 크기 0)를 넣으면 된다 — 그룹·피벗용.

### 6.2 설계

| 항목 | 결정 |
|---|---|
| 리그 노드 트리 | 덤프의 `parent` 그대로 중첩. 오브젝트 항목 자신이 `__root`다(유니티에서 리그 루트 GameObject가 곧 오브젝트이듯). 색은 유지: 연기 축 파랑·배치 축 회색·키잉 금지(`CharSlot_Track_Focus`) 흐림. 노드 항목은 기본 펼침, 오브젝트 항목은 기본 접힘(유니티). 우클릭 "노드 전부 펼치기/접기". |
| 오브젝트 계층 | `SceneObject.parent`(부모 오브젝트 id) + `parentNode`(부모 리그의 노드 id, 비면 루트). 미리보기는 `AddRigTo(…, attachParentKey: 부모.KeyOf(parentNode))`. 부모 노드가 리그에 없으면 부모 루트로 떨어지고 상태 줄에 알린다. 자식은 **부모의 stage/layer·깊이를 따른다**(자기 layer는 무시, 채널 박스에서 잠김). |
| 빈 오브젝트 | `rig = ""`. 트리에는 `{id}/__root` 하나(가운데 앵커, 크기 0). 배치 = 그 노드. 뷰포트에는 마름모 기즈모 + 이름. 모션은 못 입는다. |
| 배치 축이 없는 리그 | (배경·화면 효과) 배치 값을 리그 `__root`에 넣는다 — 전에는 무시했다. |
| 그리는 순서 | 최상위는 레이어 순(far→close)·목록 순, 자식은 부모 바로 뒤(부모 위에 그려진다). 클릭 선택은 그 역순. 부모가 숨으면 자손도 숨는다. |
| 우클릭 메뉴(유니티) | 이름 바꾸기(F2) · 복제(Ctrl+D) · 삭제(Del) ─ 숨기기/보이기 · 레이어 › far…close · 부모에서 떼기 ─ 빈 오브젝트 만들기(Ctrl+Shift+N) · 리그 오브젝트 › rigKind… ─ 노드 전부 펼치기/접기. **오브젝트나 노드 위에서 만들면 그 자리의 자식**, 레이어 폴더 위에서는 그 레이어의 최상위, 빈자리에서는 선택 오브젝트의 레이어. 헤더의 `+` 버튼도 같은 메뉴. |
| 드래그 | 하이어아키에서 오브젝트를 끌어 **다른 오브젝트/노드에 놓으면 자식**, **레이어 폴더에 놓으면 그 레이어의 최상위**, Scene/stage에 놓으면 최상위. 포인터 이벤트로 직접 구현(헤드리스 테스트 가능; OS 드래그 API는 안 쓴다). |
| 채널 박스 | id(이름 바꾸기) · 부모 콤보 · 부모 노드 콤보 · X/Y/Rot Z/Scale · Layer(최상위만) · 보이기. 이름을 바꾸면 자식의 `parent`와 초상 매핑도 따라간다. |
| 편집기 명령 | `AddSceneObject(scene, id, rig, layer, parent, parentNode)` · `RemoveSceneObject`(자손까지 한 번에) · `RenameSceneObject` · `DuplicateSceneObject`(자손까지, 새 id = 숫자 올림) · `SetObjectParent`(자기 자신·자손 금지) — 각각 되돌리기 한 번. 부모를 바꿀 때 배치 값은 그대로다(월드 위치 보존은 하지 않는다 — 유니티의 "Set Parent (world position stays)"와 다르다; 필요하면 다음). |
| 저장 | `scenes[].objects[].parent / parentNode` 선택 항목. 없으면 최상위. 산출물 `motions/1`은 그대로. |

### 6.3 하지 않는 것

- 리그 노드 자체의 추가·삭제·재배치 — 리그는 덤프(유니티 익스포터)가 정한다. 툴은 그 밑에 **오브젝트**를 붙일 뿐이다.
- 노드 단위 멀티 선택·복수 드래그 — 하나씩.

---

## 7. 검토 — 기본 리그 내장 · 무대 뼈대(PresentationUIRoot) · "부모 먼저, 리그는 자유롭게" (소유자 요청, 2026-10-28)

요청: ⓐ 리그 덤프는 거의 완성이니 **덤프 없이도 기본으로** 제공. ⓑ 실제 유니티 씬의 `PresentataionUIRoot`는 캐릭터 리그가 없어도 `StageShot › StagePan › StageZoom › Stage00~02 › Mask › DepthSystem › Depth_{Far…Close} › FramingTransform › FramingScale › Content` 구조를 갖는다 — 그 구조를 참고하되, **만들 수 있는 것은 캐릭터 리그뿐**이고, 어느 `Stage{NN}Depth_{Layer}_Content`를 부모로 할지만 정한 뒤 리그를 자유롭게 세워 **프리비즈처럼 화면을 구성하고 그 위에 모션을 Import**한다.

### 7.1 프리팹 조사 (`PresentataionUIRoot.prefab`, `tools/prefab-tree.py`로 읽음)

```
PresentataionUIRoot
├ StageViewportLayer
│ └ StageShot_Root › StagePan_Root › StageZoom_Root
│    ├ Stage00_Root
│    │ ├ Stage00Mask_Root › Stage00DepthSystem_Root
│    │ │   ├ Stage00Depth_Far_Root   › _FramingTransform › _FramingScale › Stage00Depth_Far_Content
│    │ │   ├ Stage00Depth_Back_Root  › … › Stage00Depth_Back_Content
│    │ │   ├ Stage00Depth_Mid_Root   › … › Stage00Depth_Mid_Content
│    │ │   ├ Stage00Depth_Front_Root › … › Stage00Depth_Front_Content
│    │ │   └ Stage00Depth_Close_Root › … › Stage00Depth_Close_Content
│    │ └ Stage00MaskEdge_Root › Stage00MaskEdgeGraphic
│    ├ Stage01_Root (같은 꼴)
│    └ Stage02_Root (같은 꼴)
├ StageOverlayLayer › SpriteRig_Root · TextRig_Root
└ ScreenEffectLayer › ScreenOverlay_Root
```

- RectTransform 85개. **전부 스트레치 풀 항등**(anchor 0–1, pos 0, size 0, pivot .5, scale 1, euler 0). 컴포넌트는 MonoBehaviour 13(마스크·그래픽류), CanvasRenderer 6, CanvasGroup 4 — 좌표에는 영향이 없다.
- 코어의 전제와 정확히 맞는다: `StageReducer.Slot` "v1 가정 1: 컨테이너는 항등이므로 리그를 루트 공간 직속으로 세운다". 즉 **리그를 `…_Content` 밑에 세우든 루트 직속에 세우든 좌표는 같다.** 뼈대를 트리에 넣는 것은 좌표가 아니라 **모양(하이어아키)과 자리(어느 Content인지)**를 위한 것이다.
- 코어는 뼈대 노드 이름을 모른다(`Stage00…` 문자열이 없다). 슬롯 부착은 `SlotAttachment(stageKey "stage00~02", layerKey "far…close")`라는 **라벨**이고, 커맨드 기본은 `slot(slotKey, stage="stage00", layer="mid")`. 깊이는 `CharSlot_DepthY/DepthScale`(리그 안)에 접히고, 샷은 `StageState.Shot`(zoom·pan) 한 장이다 — 런타임이 그것을 `StageZoom_Root`·`StagePan_Root`에 어떻게 입히는지는 코어에 없다(런타임 쪽).
- 툴의 `SceneObject.Stage/Layer`("stage01"/"mid")는 이 라벨과 같은 뜻이고, 뼈대의 `Stage{NN}Depth_{Layer}_Content`로 1:1 대응한다(`stage01` ↔ `Stage01`, `mid` ↔ `Mid`).

### 7.2 지금 덤프(`tests/…/Fixtures/rig-schemas.json`)

`capturedUnderParentSize` 1920×1080, 리그 셋: `character`(26 노드, `Assets/Ked.Presentation/Prefabs/CharacterRig.prefab`), `background`(19), `screenEffect`(5). 옆에 `presets/depth.json`·`presets/focus.json`(테스트 픽스처). **이것이 현재 익스포터 산출물 그대로인지 소유자 확인이 필요하다**(질문 1).

### 7.3 설계 검토 — 결론과 근거

| # | 제안 | 근거·주의 |
|---|---|---|
| A | **기본 리그 내장** — `rig-schemas.json`·`base-resolution.json`·`presets/depth.json`·`presets/focus.json`을 `Mt.Authoring`의 **임베디드 리소스**로 넣고, 리그 덤프를 싣지 않은 문서는 그것으로 시작한다. 문서에는 `rigSchemas: { path: "builtin:rig-schemas", sha256 }`로 적어 지금처럼 SHA로 대조한다. "리그 덤프…" 버튼은 그대로 — 파일을 실으면 내장본을 덮는다. | 내장본은 **ExportedTuning의 사본**이다(`vendoring.md`와 같은 규칙: 손으로 고치지 않고, 코어 동기화 때 같이 갱신하며 원본 커밋을 적는다). 위험은 **표류**(유니티 프리팹이 바뀌었는데 내장본이 옛것) — 상태 줄에 "내장 리그(원본 커밋 …)"을 늘 보이고, 문서가 다른 SHA를 기억하면 지금처럼 경고한다. |
| B | **무대 뼈대를 데이터로** — 7.1의 85 노드를 **덤프와 같은 형식**(`rigKind: "stage"`, 노드 id·parent·항등 트랜스폼)으로 내장한다. `RigSchemaLoader.AddRigTo`가 그대로 세우므로 새 로더가 없다. 지금은 프리팹에서 `tools/prefab-tree.py`로 뽑아 넣고, 나중에 Unity 익스포터가 `rigKind: "stage"`를 덤프에 더하면 그것이 내장본을 대신한다. | 코어는 안 바뀐다(반입본 불변). 뼈대가 항등이라 좌표 불변이 보장되고(7.1), 런타임이 언젠가 `FramingTransform`·`Pan`·`Zoom`을 움직이면 그 자리에 값이 들어갈 곳이 생긴다. |
| C | **씬 = 뼈대 + 캐릭터 리그 인스턴스** — 미리보기 트리는 `stage/…` 접두사로 뼈대를 먼저 세우고, 오브젝트는 `Stage{NN}Depth_{Layer}_Content` 밑에 `AddRigTo(attachParentKey)`로 붙인다(S6에서 만든 길). 모델은 **그대로**(`stage`·`layer` 라벨) — 파일 형식이 안 바뀌고 런타임 `slot c1 stage01 mid`와 같은 뜻이 유지된다. `parent/parentNode`(S6)는 리그 밑의 리그(소품)용으로 남는다. | 뼈대 노드는 **오브젝트가 아니다**: 선택·삭제·이동·채널 추가 대상이 아니고, 우클릭 메뉴는 "여기에 캐릭터 리그 만들기"만 준다. |
| D | **하이어아키 = 유니티 그대로 + 압축 보기** — 기본은 `PresentataionUIRoot › StageViewportLayer › StageShot_Root › StagePan_Root › StageZoom_Root › Stage00_Root › Stage00Mask_Root › … › Content › c1`의 전체 사슬(비어 있는 스테이지·레이어도 보인다 — 거기 우클릭해 만들 수 있어야 하므로). 12단이 깊으므로 **"압축 보기"** 토글: `Stage00 › Far › Content › c1`처럼 중간 항등 노드를 접는다(툴 표시만, 데이터는 같다). Overlay·ScreenEffect 레이어도 보이되 만들기는 없다. | 지금의 `Scene › stage › layer` 폴더는 압축 보기의 특수형이다. 펼침 기억은 그대로 쓴다. |
| E | **만들기 = 부모 먼저, 리그는 자유롭게** — `…_Content`(또는 스테이지/레이어 폴더) 우클릭 → "캐릭터 리그 만들기" 하나. id 자동(c1, c2…), 그 자리에 선다. 만든 뒤 채널 박스에서 초상·배역·배치를 정하고 드래그로 다른 Content로 옮긴다(= 스테이지/레이어 바꾸기, 월드 위치 유지). `background`·`screenEffect`·빈 오브젝트 만들기는 **메뉴에서 뺀다**(모델과 명령은 남겨 두고 "고급" 설정으로 숨긴다 — 소유자 결정: 캐릭터부터). | 런타임 `slot` 폴드도 `character`만 세우므로 툴과 런타임이 같은 제약을 갖는다. |
| F | **프리비즈 → 모션 Import** — 흐름은 이미 있다(씬 구성 → `motions.json 가져오기` → rigKind별 목록 → 오브젝트 골라 입힘). 더할 것: 가져오기 직후 **고른 오브젝트에 자동으로 입혀 보기**, 씬을 **프리셋으로 저장/불러오기**(문서 간 재사용). 선택: 씬을 **Yarn 커맨드 열로 내보내기**(`slot c1 stage01 mid` · `cast` · `place` · `size`) — 씬을 런타임에 "보내지 않는다"는 결정과 어긋나지 않는 **텍스트 산출물**이고, 커맨드 문법은 코어 파서가 안다. 소유자 판단에 맡긴다(질문 5). | — |
| G | **새 문서의 기본 씬** — 지금은 `c1 · character` 하나. 뼈대가 생기면 "캐릭터 없이 뼈대만"이 유니티와 같다. 다만 모션을 열자마자 보려면 리그 하나는 있어야 하므로, **빈 뼈대 + 첫 모션/가져오기 때 "캐릭터가 없다 — Stage01 Mid에 c1 만들기" 한 줄 버튼**을 권한다. 예전 문서(씬 없음)는 지금처럼 c1로 옮긴다. | 소유자 결정(질문 4). |
| H | **카메라를 트리로** — 지금 샷 zoom·pan은 뷰포트의 보기 변환이다. 뼈대가 생기면 `StageZoom_Root`(배율)·`StagePan_Root`(위치)에 넣는 것이 런타임과 같은 자리다. 그러나 **런타임이 정확히 어느 노드에 어떤 피벗으로 입히는지**를 코어가 말하지 않으므로 확인 전에는 보기 변환으로 둔다. | 질문 3. 마스크(`Stage00Mask_Root`)는 Pan/Zoom 아래라 카메라와 같이 움직인다 — 트리로 옮기면 클리핑까지 맞출 수 있다. |

### 7.4 소유자에게 묻는 것

1. **내장할 덤프** — `tests/…/Fixtures/rig-schemas.json`(+ `presets/depth.json`·`focus.json`)이 현재 익스포터 산출물 그대로인가? 아니면 최신 `ExportedTuning/` 폴더를 주시면 그것을 내장하고 원본 커밋을 적는다.
2. **뼈대의 출처** — 지금은 올려 주신 프리팹에서 뽑아 `rigKind: "stage"` 형식으로 내장한다. 나중에 Unity 익스포터가 같은 것을 덤프에 더할지(권장 — 표류 방지) 결정.
3. **샷 노드** — 런타임이 zoom·pan을 `StageZoom_Root`·`StagePan_Root`에 어떻게(배율·피벗·단위) 입히는가? 알려 주시면 카메라를 트리로 옮긴다(H). 모르면 보기 변환 유지.
4. **새 문서 기본** — 빈 뼈대(+ "c1 만들기" 버튼) vs 지금처럼 c1 하나.
5. **Yarn 커맨드 열 내보내기**(F 선택) — 할지 말지.
6. **메뉴 축소** — background·screenEffect·빈 오브젝트 만들기를 숨길지(권장) 아예 뺄지.

### 7.5 단계(안) — 결정 뒤

| 단계 | 내용 | 끝났다는 증거 |
|---|---|---|
| **S8a** 기본 리그 내장 | 임베디드 리소스 4개 · `builtin:` 참조와 SHA 대조 · 상태 줄 "내장 리그(원본 …)" · 덤프 파일을 실으면 덮기 | 덤프 없이 새 문서 → 바로 c1이 서고 모션 열림. 파일 덤프와 내장본 SHA 비교 테스트 |
| **S8b** 무대 뼈대 | `stage` 리그 데이터(프리팹에서 생성, `tools/prefab-tree.py`) · 미리보기 트리에 뼈대 먼저 · 오브젝트는 `…_Content` 밑 · 하이어아키 전체 사슬 + 압축 보기 · 빈 스테이지/레이어도 표시 | 뼈대를 넣기 전후 모든 오브젝트 좌표가 같다(항등 증명 테스트). 하이어아키 경로 테스트 |
| **S8c** 만들기 흐름 | Content 우클릭 "캐릭터 리그 만들기" · 다른 만들기는 고급으로 · 새 문서 기본(결정 4) · 가져오기 직후 자동 입히기 · "캐릭터가 없다" 버튼 | 헤드리스: Content 우클릭 → 만들기 → 그 스테이지/레이어 |
| **S8d** 선택 | 카메라를 트리로(결정 3) · 씬 프리셋 저장/불러오기 · Yarn 열 내보내기(결정 5) | — |

### 7.6 하지 않는 것

- 코어 반입본 수정(뼈대 이름을 코어에 넣지 않는다 — 데이터로 간다).
- 뼈대 노드의 편집·삭제·키잉 — 뼈대는 런타임이 정한다.
- 비-character 리그의 재생 — 이전 결정 그대로.

### 7.7 추가 조사 — 실제 게임 진행 중 프리팹(캐릭터 둘 + 배경, 2026-10-28 두 번째 업로드)

RectTransform 161개. 뼈대(85)는 첫 프리팹과 같고 전부 항등이다. 그 위에 선 인스턴스:

| 인스턴스 | 부모 | 유니티 이름 규칙 | 눈에 띄는 값 |
|---|---|---|---|
| `s1_CharacterRig` | `Stage00Depth_Mid_Content` | `{slot}_{노드}` (덤프의 `__root` = `{slot}_CharacterRig`) | `CharSlot_Track_Focus` (518.4, −340) · `CharSlot_DepthY` (0, −580.56) · `CharSlot_DepthScale` 1.708 · `CharSlot_Track` **항등** · `Sprite_Image` sizeDelta (772.03, 0), preserveAspect 1 |
| `s2_CharacterRig` | 같은 Content | | `Track_Focus` (−518.4, −340), 나머지 s1과 같음 |
| `bg_main_BackgroundRig` | `Stage00Depth_Far_Content` | `{slot}_{노드}` | `BackgroundSprite_Image` sizeDelta (1702.8, 0), preserveAspect 0 · `Background_DepthScale` 항등 · `Background_ObjectSlotRoot` scale 2 (덤프 초기값 그대로) |
| `ScreenEffectRig` | `ScreenEffectLayer` | 접두사 없음 | 항등. 레이어에 정적 `ScreenOverlay_Root`가 하나 더 있다(이름 중복) |

CanvasGroup 13개(alpha 0/1 섞임 — 가시성은 alpha로).

**설계에 바로 영향을 주는 것**

1. **캐릭터 자리는 `place`가 정한다, `move`가 아니다.** 실제 위치는 `CharSlot_Track_Focus`(place 폴드 `PlaceFocusStageReduction`이 "focus 프리셋이 화면 지점에 오도록" 푼 값)에 있고 `CharSlot_Track`(move·nudge)은 항등이다. 툴의 배치 X/Y는 지금 `CharSlot_Track`에 들어가므로 "런타임과 같은 자리"를 만들려면 **place 개념이 필요하다**: 채널 박스에 `자리: 화면 지점(left/center/right/top/bottom/tl…, 또는 좌표) × focus 프리셋(bust/face/body…)`을 두고 코어 `PlaceFocusStageReduction`으로 `Track_Focus`를 풀어 넣는다(툴이 좌표를 짓지 않는다). 자유 오프셋 X/Y는 그대로 `CharSlot_Track`(= move). 이렇게 하면 씬이 곧 `slot → cast → place → size → move` 열과 1:1이 된다 — 7.3 F의 Yarn 내보내기가 자연스러워진다.
2. **깊이는 S7의 focus 표가 맞았다.** `DepthY = −(1.708 − 1) × 820 = −580.56` — focus 보존 bust(base 820)가 들어간 값이다. focus.json이 없던 S4의 0은 게임과 달랐다. **내장 기본값에 `presets/focus.json`이 반드시 들어가야 한다**(질문 1에 포함).
3. **배경도 프리비즈 대상이다.** 배경 리그는 `Far_Content`에 서고 크기는 런타임 사이징(코어에 없다)으로 정해진다. 프리팹 값으로 역산하면 `폭 = 높이 × 종횡비` 꼴(이미지 rect 1920 + 1702.8 = 3622.8 = 1080 × 3.354, preserveAspect 0 → 그대로 채움). 7.3 E의 "캐릭터만"을 **캐릭터 + 배경**으로 넓히기를 권한다: `Far_Content` 우클릭 "배경 리그 만들기(PNG)". 배경 사이징 공식은 런타임 확인이 필요하다(질문 7). 배경은 깊이 폴드를 타지 않는다(`Background_DepthScale` 항등).
4. **기본 스테이지는 `stage00`.** 실제 게임이 Stage00을 쓰고 코어 `slot` 기본도 `"stage00"`이다. 툴의 기본 `stage01`은 **`stage00`으로 바꾼다**(예전 문서는 적힌 값 그대로).
5. **이름 규칙.** 툴 키 `s1/CharSlot_Track`은 유니티 이름 `s1_CharSlot_Track`과 같은 뜻이다. 하이어아키 표시는 유니티처럼 `s1_…`로 보여도 되고(표시만), 오브젝트 id는 슬롯 키(`s1`, `bg_main`)다 — 새 오브젝트 기본 id를 `c1`이 아니라 **`s1`, `s2`…**(배경은 `bg_main`, `bg_2`…)로 바꾸면 Yarn과 같아진다.
6. **초상 종횡비.** `772.03 = 1080 × 0.7148` — `PortraitSizingReduction`(높이 맞춤) 그대로다. 툴은 PNG의 종횡비를 쓰므로 PNG가 실제 스프라이트면 같은 값이 나온다. 런타임은 `portrait-dimensions.json`(배역·변형·표정별)을 쓰므로, 내장 기본값에 그 파일도 넣으면 PNG 없이도 자리표시 크기가 맞는다(질문 1에 포함).
7. **화면 효과 리그**는 `ScreenEffectLayer` 밑에 접두사 없이 선다. 뼈대 데이터에 `ScreenEffectLayer › ScreenOverlay_Root`를 두되 만들기는 없다.

**7.3 표의 수정**: E → "캐릭터 리그 만들기" + "배경 리그 만들기"(Far 권장, 어느 Content든 가능). 채널 박스 → `자리(place: 화면 지점 × focus)` 행 추가, X/Y는 "오프셋(move)"로 이름을 바꾼다. 7.5 S8c에 place 행을 더한다.

**7.4에 더하는 질문**: 7. 배경 사이징 공식(런타임 `BackgroundRig` 사이징 정책)을 알려 주시거나, 역산값(`폭 = 높이 × 종횡비`, preserveAspect 0)으로 가도 되는지. 8. 새 오브젝트 기본 id를 `s1`·`bg_main`처럼 Yarn 슬롯 키 꼴로 바꿀지.

### 7.8 런타임 확인 — `ked-presentation-runtime`(`4b27e23`)·`ked-stage-runtime`(`ec9ada2`) 코드로 푼 질문

소유자가 샷 관련 스크립트를 주었고 두 런타임 저장소를 읽었다. 7.4의 질문 가운데 **1·3·7은 코드가 답한다.**

| 질문 | 답 | 근거 |
|---|---|---|
| 1. 내장할 덤프 | **툴 픽스처 = 현재 익스포터 산출물 그대로다.** `rig-schemas.json` SHA-256 `f9457cf9…`가 세 곳(툴 픽스처 · presentation-runtime `ExportedTuning/` · stage-runtime `ExportedTuning/`) 모두 같다. 내장할 묶음은 `ExportedTuning/`의 `base-resolution.json` · `rig-schemas.json` · `portrait-dimensions.json` · `presets/depth.json` · `presets/focus-tuning.json` · `presets/role-anchor.json`(+ 원본 커밋·`export-report.json`의 `exportedAtUtc` 2026-08-21). 나머지(`ease-golden`·`mask-motion`·`screen-*`·`surface-layout`·`visual-focus`)는 툴 범위 밖. | `sha256sum` · `export-report.json` |
| 3. 샷을 입히는 자리 | **`StageZoom_Root.localScale = (1 + 0.05·zoom)` (피벗 가운데) · `StagePan_Root.anchoredPosition = panInRigSpace` · 계산 공간(RigSpaceRoot) = `StageShot_Root`.** Pan이 Zoom 위에 있으므로 점 p → `pan + scale·p` — 지금 툴의 보기 변환(`root·scale + pan`)과 **같은 식**이다. 트리로 옮기면 값은 안 바뀌고 마스크 클리핑만 따라온다. | `PresentationCameraRootApplier.Apply` · `PresentationUIRoot.ShotResponseStage.cs` |
| 7. 배경 사이징 | **캐릭터와 같은 정책**(`CharRigImageSizingMode.HeightFitPreserveAspect`: `sizeDelta = (부모 높이 × 종횡비, 0)`, `preserveAspect = true`). 툴의 `PortraitSizingReduction` 경로를 `BackgroundSprite_Root/Image`에 그대로 쓰면 된다. 7.7의 "stretch 보정" 추정은 틀렸다 — 같은 식이다. 배경 슬롯은 `SetupBackgroundRigCommandSpec` 기본 `Stage00/Far`, 부모는 `GetDepthContent(stage, layer)` = `Stage{NN}Depth_{Layer}_Content`. | `SetBackgroundSpriteCommandBgR.sizingMode` · `CharRigImageSizingPolicy` · `BackgroundRigSlotResolver` |

**새로 드러난 것 — 깊이 층의 샷 응답(패럴랙스).** `FramingTransform`·`FramingScale`은 항등이 아니다: 샷이 움직이면 `StageDepthLayerBinder`가 층마다 `PresentationResponseProfile`로 **pan 따라가기·focus 산개·zoom 상쇄/강조**를 입힌다(`PresentationResponseMath`):

```
destPos   = base + sign(base − focus) ⊙ focusSpreadPixelsPerZoom × (scale − 1) + pan ⊙ panResponse
destScale = baseScale × scale ^ zoomScaleResponse
Far  : spread(−8,0) pan(−0.04,0) zoom −0.15     Back : (−4,0) (−0.02,0) −0.08
Mid  : 0            0            0              Front: (4,0)  (0.02,0)  0.04
Close: (16,0)       (0.2,0)      0.16
```

샷이 기본(zoom 0·pan 0)이면 전부 항등이라 코어 가정이 맞고, **샷이 들어가면 Mid 외의 층은 코어 폴드와 다르게 보인다**(코어 `StageState`에는 이 축이 없다 — `StageStateComparer`도 Framing 노드를 캡처할 때만 본다). 툴은 카메라를 보기 전용으로 두므로 **프리비즈에서 카메라를 움직일 때 이 식을 Framing 노드에 넣으면 게임과 같은 패럴랙스가 보인다.** 값은 코드 상수(런타임 소유)라 툴은 같은 상수를 데이터로 갖고 원본 파일을 적는다. 코어에는 넣지 않는다(반입본 불변).

**익스포터에 요청할 수 있는 것(질문 2의 구체화)**: `PresentationTuningExporter.ExportRigSchemas`가 `CaptureRig(rigKind, prefabPath, root, ids)`로 리그를 캡처하므로, 같은 함수로 `PresentationUIRoot` 프리팹을 `rigKind: "stage"`로 캡처해 덤프에 더할 수 있다(`Refs` 열거형이 곧 노드 id 목록). 그 전까지 툴은 `tools/prefab-tree.py` 산출을 내장한다.

**이름 규칙 확인**: 캐릭터는 `{slot}_{노드}`(`BuildCharacterRigRoot`), 배경은 `{rigKey}_{노드}`(`ResolvedRolePrefix`, `rigRootName` 기본 `BackgroundRig`). 툴 키 `{id}/{노드}`와 1:1.

**VnTool 선례**: `vnstoryeditor_avalonia`는 `Assets/DefaultTuning/**/*.json`을 `EmbeddedResource`(LogicalName `DefaultTuning/…`)로 넣고 `AuthoringSession.CreateDefaultTuning()`이 프로젝트 옆 규약 폴더로 풀어 쓴다. 이 툴도 같은 꼴로 간다 — 다만 폴더로 풀지 않고 **메모리에서 바로 싣는다**(덤프 파일이 없는 상태가 기본이므로). 문서에는 `rigSchemas: { path: "builtin:ExportedTuning@<원본 커밋>", sha256 }`.

**7.4 갱신**: 남은 질문은 **2(익스포터에 stage 추가 여부) · 4(새 문서 기본) · 5(Yarn 내보내기) · 6(메뉴 축소) · 8(기본 id 꼴)**. 나머지는 코드가 정했다.

## 결정 (2026-10-28, 소유자)

- 모션의 주인은 **rigKind**. 공통 스키마를 가진 리그끼리는 모션을 공유할 수 있다 → 툴은 "rigKind가 같거나 채널 노드가 전부 있으면" 그 오브젝트에 입혀 보인다(`ScenePreview.CanWear`).
- 씬은 **미리보기 전용**. 유니티 런타임에 보내지 않는다.
- 비-character 리그 재생은 **당분간 고려하지 않는다**. 캐릭터 리그부터 제대로 한다.

## 상태

- **S1 완료**(`dev`): `SceneAsset/SceneObject` · 편집기 씬 명령(추가/삭제/배치/레이어/보이기/초상, 각각 되돌리기 하나) · 저작본 `scenes[]` 왕복과 예전 `preview.portrait` 이관 · `ScenePreview`(코어 `AddRigTo`로 접두사 트리, 배치 축 노드, 초상 사이징, 선택 오브젝트에만 모션) · 뷰포트 격자/축/프레임 · 오브젝트 클릭 선택 · 왼쪽 오브젝트 목록 · 오른쪽 채널 박스(X/Y/Rot/Scale/Layer/보이기).
- **S2 완료**: 왼쪽 하이어아키 `TreeView`(Scene › stage › layer › 오브젝트 › 연기 축/배치 축 노드, 펼침 상태 기억) · 오브젝트 클릭 = 선택 · 노드 클릭 = 매니퓰레이터 노드 + 채널 추가 대상 · 더블클릭 = 채널 추가.
- **S3 완료**: 모션 목록 rigKind 순 묶음·"안 맞음" 표시 · 새 모션 = 선택 오브젝트의 rigKind · 툴바 내보내기 범위(전체/이 rigKind/이 오브젝트가 입는 모션) — `MotionExport.TryExport(filter)`는 고른 모션의 문제만 막는다.
- **S4 완료**: `DepthTuningFile`(덤프 옆 `presets/depth.json`) → 레이어 깊이를 코어 `SetDepthStageReduction`(focus 보존 bust, 캐릭터별 오프셋 없이)으로 — 런타임 size 폴드와 같은 값 · 카메라 zoom/pan 보기(`ShotIntentMath`)와 `SetCamera` 명령.
- **S6 완료**: 리그 노드를 덤프의 `parent` 그대로 중첩(연기/배치 폴더 제거, 오브젝트 항목 = `__root`) · `SceneObject.parent/parentNode`와 빈 오브젝트(`rig = ""`) · 편집기 `SetObjectParent`·`RenameSceneObject`·`DuplicateSceneObject`·자손까지 `RemoveSceneObject` · `ScenePreview`가 코어 `AddRigTo(attachParentKey)`로 자식을 부모 노드 밑에 세움(없는 노드는 부모 루트, `fallenBack`) · 배치 축 없는 리그는 루트에 배치 · 뷰포트 빈 오브젝트 기즈모·조상 가시성 · 하이어아키 우클릭 메뉴(만들기는 그 자리의 자식)·`+` 버튼·Del/Ctrl+D/Ctrl+Shift+N/F2·포인터 드래그로 부모/레이어 옮기기 · 채널 박스 id/부모/부모 노드.
- **S7 완료**: 부모 바꾸기의 월드 위치 보존(`ScenePreview.PlacementKeepingWorld` — 새 부모로 세운 트리에서 루트 월드 점의 차이를 배치 노드의 부모 공간으로 되돌림, `SetObjectParent(position)` 한 번에) · 드래그 중 놓을 자리 `⇥` 표시 · 씬 목록 명령(`AddScene`·`DuplicateScene`·`RemoveScene`·`RenameScene`·`SetActiveScene`)과 씬 콤보/이름 칸/Scene 우클릭 메뉴 · `FocusTuningFile`(`presets/focus.json`) → 깊이 폴드의 `focusTuning`(배역 `SceneObject.character`는 있었으나 뒤에 통째로 뺐다 — `characterKey`는 빈 문자열).
- **S8 완료**(결정: Yarn 내보내기 없음 · background/screenEffect/빈 오브젝트 만들기 제거 · 기본 id `s1…` · 뼈대는 프리팹에서 뽑아 내장): `BuiltinTuning`(임베디드 `Defaults/ExportedTuning` + `stage-skeleton.json`, 세션 기본값·파일 덤프 우선) · `StageNodes`(라벨 ↔ 유니티 이름) · `ScenePreview`가 뼈대를 `stage/`로 먼저 세우고 최상위를 Content에 붙임 · `StageShotResponse`(Zoom/Pan 노드 + 층 패럴랙스) · `SceneObject.place/placeFocus` + 코어 place 폴드(깊이 → place 순서, 게임 프리팹 DepthY −580.56 재현 테스트) · 기본 `stage00`·`s1` · 하이어아키 뼈대 기반(압축 보기 토글) · 채널 박스 자리/오프셋/Stage 행 · 만들기는 캐릭터 리그만.
- **S8+**: 부모 바꿀 때 회전·배율까지 보존(`Placement` 한 벌, 부모 공간의 각·배율 차이를 되돌림) · `+ 캐릭터 리그` 버튼이 메뉴 없이 바로 만든다.
- **정리(소유자 요청)**: 씬 콤보·이름 칸·Scene 우클릭 씬 메뉴 제거(모델·명령은 유지) · 압축 보기 제거(전체 사슬 고정, 오브젝트 없는 가지는 기본 접힘) · 격자·축 토글 제거(항상 켬) · 모션 추가 폼(이름·리그·길이·끝) 제거 — 기본값으로 만들고 인스펙터에서 바꾼다 · 채널 mode "(모션 기본)" 제거(relative/absolute만, 처음엔 모션 기본) · 채널 종류 표시 euler → rotate(`ChannelKindLabel`, 파일은 euler).
- **S9 — 마야식 채널 박스**(소유자 요청): 오른쪽 "뷰포트 조작"(노드 콤보)을 **채널 박스**로 — 하이어아키에서 고른 리그 노드의 Translate X/Y · Rotate X/Y/Z · Scale X/Y(지금 시각의 포즈 값, `ViewportView.ComponentValue`). 입력은 `ViewportView.SetComponent` → 매니퓰레이터를 놓은 것과 같은 길(`CommitPose`: Auto Key/키 있는 시각 = 키, 아니면 키 없는 포즈). ● = `KeyComponent`(Key Selected). 칸 색 = 이 시각에 키/어딘가에 키/키 없는 포즈. **"채널 추가" 폼(노드·종류·mode 콤보) 제거** — 채널·성분은 첫 키에서 생긴다(편집기 `AddChannel`은 가져오기·테스트용으로 남는다). 아웃라이너의 rel/abs 토글 제거, 모션 인스펙터 "기본 mode" → "mode"(`MotionEditor.SetMode`: 기본 mode와 모든 채널 mode를 한 번에).
- **M2 — mode는 쓰는 쪽(커맨드)이 정한다(소유자 결정, 2026-10-28, 규약 `motions/2`)**: 무대 `ked-stage-runtime` `dev` @ `4ff3c2a` — 코어 `MotionChannel.Mode` 제거, `MotionMath.Compose/EndValue/ClaimFor(channel, mode, …)`, `MotionModeToken`(relative|absolute, 생략 relative), `MotionRules`에서 중립값 규칙 셋 제거, 리듀서·파서 표 `motion(slot, name, mode?)`, `MotionCommandCharR`는 스펙 mode로 합성하고 restore는 시작값을 되씀, 로더 `motions/2`(v1의 mode 무시), 유니티 내보내기 mode 제거(상수 성분도 싣는다). 재생 반입본 `ked-presentation-runtime` `dev` @ `b7a2622`(Yarn `motion` 인자 셋, 견본 motions/2). 이 툴: 코어 재반입, `motions/2` 쓰기·읽기(v1 읽힘), 저작본 `motion-authoring/2`(채널 mode 없음 · 모션 `previewMode` = 뷰포트 렌즈, v1 읽힘), 검증 경고(restore 마지막 키 — 막지 않음), 돕는 손 정리(첫 키 중립·채널 relative 제거). 유니티 테스트 통과·`dev` 합침(소유자 확인, 2026-10-28).
- **정리 2(소유자 결정, 2026-10-28)**: **배역**(`SceneObject.character`·`SetObjectCharacter`·focus 배역 콤보)과 **오브젝트 계층**(`parent`·`parentNode`·`SetObjectParent`·`PlacementKeepingWorld`·하이어아키 드래그·"부모에서 떼기"·자손 따라가는 삭제/복제/이름바꾸기)을 모델·저작본·UI·테스트에서 통째로 뺐다. 이유: 런타임에 대응물이 없고(슬롯은 (stage, layer)에만 붙는다) 캐릭터 리그만 만드는 지금은 쓸 일이 없다 — 이 툴은 리그 하나의 모션 제작에 집중한다. 옛 저작본이 없어 마이그레이션 없음. 필요해지면 이 커밋 전 이력에서 되살린다. 재생 기준(상대값 relative / 절대값 absolute, 옛 "미리보기 mode")은 명시적 설정으로 남긴다(소유자).
- 남은 것: 노드 단위 멀티 선택 · 익스포터가 `rigKind: "stage"`를 덤프에 넣으면 내장 뼈대를 그것으로 교체.
