#!/usr/bin/env python3
"""PresentataionUIRoot.prefab(유니티 YAML) → 덤프 형식의 무대 뼈대 리그(rigKind "stage").

쓰는 법: python3 -I tools/prefab-to-stage-rig.py <prefab> <out.json>
노드 id는 유니티 이름 그대로, parent는 부모 이름(루트는 비움). 트랜스폼 값은 프리팹에서 읽는다(전부 항등이어야 정상).
캐릭터·배경 리그 인스턴스(이름에 '_CharacterRig'·'_BackgroundRig'·'ScreenEffectRig')는 뼈대가 아니므로 뺀다.
"""
import json, re, sys

src, out = sys.argv[1], sys.argv[2]
text = open(src, encoding='utf-8').read()
docs = re.split(r'^--- !u!(\d+) &(\d+)\n', text, flags=re.M)
objs = {docs[i + 1]: (int(docs[i]), docs[i + 2]) for i in range(1, len(docs), 3)}
names = {oid: re.search(r'm_Name: (.*)', body).group(1).strip() for oid, (t, body) in objs.items() if t == 1}
rects = {}
for oid, (t, body) in objs.items():
    if t != 224:
        continue
    def v2(key):
        m = re.search(key + r': \{x: ([-\d.e]+), y: ([-\d.e]+)\}', body); return {'x': float(m.group(1)), 'y': float(m.group(2))}
    def v3(key):
        m = re.search(key + r': \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}', body); return {'x': float(m.group(1)), 'y': float(m.group(2)), 'z': float(m.group(3))}
    children = re.findall(r'- \{fileID: (\d+)\}', body.split('m_Children:')[1].split('m_Father')[0])
    rects[oid] = dict(go=re.search(r'm_GameObject: \{fileID: (\d+)\}', body).group(1),
                      father=re.search(r'm_Father: \{fileID: (\d+)\}', body).group(1), children=children,
                      anchoredPosition=v2('m_AnchoredPosition'), anchorMin=v2('m_AnchorMin'), anchorMax=v2('m_AnchorMax'),
                      pivot=v2('m_Pivot'), sizeDelta=v2('m_SizeDelta'), localScale=v3('m_LocalScale'), localEulerAngles=v3('m_LocalEulerAnglesHint'))

def is_instance(name):
    return name.endswith('_CharacterRig') or name.endswith('_BackgroundRig') or name == 'ScreenEffectRig'

nodes = []
def walk(rid, parent_name):
    d = rects[rid]
    name = names[d['go']]
    if is_instance(name):
        return
    nodes.append({'id': name, 'parent': parent_name, 'anchoredPosition': d['anchoredPosition'], 'anchorMin': d['anchorMin'],
                  'anchorMax': d['anchorMax'], 'pivot': d['pivot'], 'sizeDelta': d['sizeDelta'], 'localScale': d['localScale'],
                  'localEulerAngles': d['localEulerAngles'], 'measuredRectSize': {'x': 1920.0, 'y': 1080.0},
                  'hasCanvasGroup': False, 'canvasGroupAlpha': 1.0})
    for c in d['children']:
        if c in rects:
            walk(c, name)

roots = [r for r, d in rects.items() if d['father'] not in rects]
assert len(roots) == 1, roots
# 덤프 규약: 리그 루트는 "__root"이고 parent가 비어 있다. 프리팹 루트(PresentataionUIRoot)가 곧 __root다.
walk(roots[0], '')
nodes[0]['id'] = '__root'
for n in nodes[1:]:
    if n['parent'] == names[rects[roots[0]]['go']]:
        n['parent'] = ''
rig = {'rigKind': 'stage', 'sourcePrefab': 'Assets/Samples/Prefabs/PresentataionUIRoot.prefab', 'nodes': nodes}
json.dump({'capturedUnderParentSize': {'x': 1920.0, 'y': 1080.0}, 'rigs': [rig]}, open(out, 'w', encoding='utf-8'), indent=2, ensure_ascii=False)
print(len(nodes), 'nodes ->', out)
