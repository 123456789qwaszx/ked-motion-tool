#!/usr/bin/env python3
"""Unity 프리팹(YAML)의 RectTransform 계층을 들여쓰기로 찍는다 — 무대 뼈대(PresentataionUIRoot)를 덤프 형식으로 옮길 때의 조사용.
쓰는 법: python3 -I tools/prefab-tree.py <prefab 경로>
"""
import re, sys, json
path = sys.argv[1]
text = open(path, encoding='utf-8').read()
docs = re.split(r'^--- !u!(\d+) &(\d+)\n', text, flags=re.M)
# docs: [pre, type, id, body, type, id, body...]
objs = {}
for i in range(1, len(docs), 3):
    t, oid, body = docs[i], docs[i+1], docs[i+2]
    objs[oid] = (int(t), body)
names = {}
comps = {}  # gameobject id -> list of component type ids
for oid, (t, body) in objs.items():
    if t == 1:
        m = re.search(r'm_Name: (.*)', body); names[oid] = m.group(1).strip() if m else '?'
        comps[oid] = re.findall(r'component: \{fileID: (\d+)\}', body)
        act = re.search(r'm_IsActive: (\d)', body); 
rects = {}
for oid, (t, body) in objs.items():
    if t == 224:
        go = re.search(r'm_GameObject: \{fileID: (\d+)\}', body).group(1)
        father = re.search(r'm_Father: \{fileID: (\d+)\}', body).group(1)
        def v2(key):
            m = re.search(key + r': \{x: ([-\d.e]+), y: ([-\d.e]+)\}', body); return (float(m.group(1)), float(m.group(2)))
        def v3(key):
            m = re.search(key + r': \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}', body); return (float(m.group(1)), float(m.group(2)), float(m.group(3)))
        rot = re.search(r'm_LocalRotation: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+), w: ([-\d.e]+)\}', body)
        euler = v3('m_LocalEulerAnglesHint')
        children = re.findall(r'- \{fileID: (\d+)\}', body.split('m_Children:')[1].split('m_Father')[0]) if 'm_Children:' in body else []
        rects[oid] = dict(go=go, father=father, children=children, anchorMin=v2('m_AnchorMin'), anchorMax=v2('m_AnchorMax'), pos=v2('m_AnchoredPosition'), size=v2('m_SizeDelta'), pivot=v2('m_Pivot'), scale=v3('m_LocalScale'), euler=euler)
# other component types per gameobject
typemap = {}
for oid, (t, body) in objs.items():
    if t not in (1, 224):
        go = re.search(r'm_GameObject: \{fileID: (\d+)\}', body)
        if go:
            m = re.search(r'm_Script: \{fileID: (-?\d+), guid: ([0-9a-f]+)', body)
            label = {114: 'MonoBehaviour', 222: 'CanvasRenderer', 223: 'Canvas', 225: 'CanvasGroup', 212: 'SpriteRenderer', 4: 'Transform'}.get(t, str(t))
            if t == 114:
                nm = re.search(r'm_Name: (.*)', body)
                label = 'Mono(' + (m.group(2)[:8] if m else '?') + ')'
            typemap.setdefault(go, []).append(label)
# find roots (father 0 or not in rects)
roots = [r for r, d in rects.items() if d['father'] not in rects]
def dump(rid, depth, out):
    d = rects[rid]; go = d['go']
    name = names.get(go, '?')
    extra = ','.join(typemap.get(go, []))
    out.append('  ' * depth + f"{name}  [a {d['anchorMin']}-{d['anchorMax']} p {d['pos']} s {d['size']} pv {d['pivot']} sc {d['scale']} e {d['euler']}] {extra}")
    for c in d['children']:
        if c in rects: dump(c, depth + 1, out)
out = []
for r in roots: dump(r, 0, out)
print('\n'.join(out))
print('objects:', len(names), 'rects:', len(rects))
