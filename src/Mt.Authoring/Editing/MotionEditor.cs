using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Serialization;

namespace Mt.Authoring.Editing;

/// <summary>
/// 문서를 바꾸는 유일한 통로 (VnTool <c>ProjectEditor</c>의 자리).
///
/// 화면들은 서로 이야기하지 않는다 — 전부 여기에 의도를 전달하고 <see cref="Changed"/>를 듣고 다시 그린다.
/// 되돌리기는 문서 전체 스냅샷(<see cref="MotionDocumentJson"/>)이다: 되돌리기 한 번 = 조작 하나이고,
/// 명령마다 역연산을 짓지 않으므로 새 명령이 생겨도 되돌리기가 빠질 수 없다.
///
/// 명령은 모델을 고치되 **규칙을 강제하지 않는다** — 키를 길이 밖에 두거나 relative 첫 키를 0 아닌 값으로 두는 것은
/// 편집 중 자연스러운 중간 상태다. 어긋남은 <c>MotionValidator</c>가 계속 알리고, 내보내기만 막는다.
/// 단 하나, 키 목록의 시각 오름차순은 편집기가 지킨다(그래프가 그것을 전제한다).
/// </summary>
public sealed class MotionEditor
{
    private const int MaxHistory = 200;

    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();

    public MotionEditor(MotionDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Document.EnsureDefaultScene();
    }

    public MotionDocument Document { get; private set; }

    /// <summary>무엇이든 바뀌었을 때. 듣는 쪽은 현재 상태를 다시 읽는다.</summary>
    public event EventHandler<MotionChangedEventArgs>? Changed;

    /// <summary>실제로 바뀐 횟수. 두 시점의 값이 같으면 그 사이에 아무 일도 없었다.</summary>
    public long Revision { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    // ── 문서 ──────────────────────────────────────────────────────────

    public void SetRigSchemas(string path, string sha256)
        => Mutate(MotionChangeKind.Document, () => Document.RigSchemas = new RigSchemasReference { Path = path, Sha256 = sha256 });

    public void SetBaseResolution(string? path)
        => Mutate(MotionChangeKind.Document, () => Document.BaseResolution = path is null ? null : new FileReference { Path = path });

    /// <summary>호환용 — 활성 씬의 선택 오브젝트가 없으니 첫 오브젝트(c1)의 초상으로 둔다.</summary>
    public void SetPreviewPortrait(string? path)
    {
        SceneAsset scene = RequireScene(null);
        SceneObject? first = scene.Objects.FirstOrDefault();

        if (first is null)
        {
            return;
        }

        SetObjectPortrait(scene.Name, first.Id, path);
    }

    // ── 씬 ────────────────────────────────────────────────────────────

    private SceneAsset RequireScene(string? name)
    {
        Document.EnsureDefaultScene();
        return (name is null ? Document.ActiveSceneAsset : Document.FindScene(name))
            ?? throw new KeyNotFoundException($"씬 '{name}'이 없다.");
    }

    private static SceneObject RequireObject(SceneAsset scene, string id)
        => scene.Find(id) ?? throw new KeyNotFoundException($"씬 '{scene.Name}'에 오브젝트 '{id}'가 없다.");

    /// <summary>오브젝트(리그 인스턴스)를 더한다. id는 [a-z0-9_]+이고 씬 안에서 하나뿐이어야 한다.</summary>
    public SceneObject AddSceneObject(string? sceneName, string id, string rig, string layer = SceneLayers.Mid)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(rig);
        SceneAsset scene = RequireScene(sceneName);

        if (!MotionNames.IsValid(id))
        {
            throw new InvalidOperationException($"오브젝트 id는 영문 소문자·숫자·_ 만 쓸 수 있다 ('{id}')");
        }

        if (scene.Find(id) is not null)
        {
            throw new InvalidOperationException($"오브젝트 '{id}'가 이미 있다.");
        }

        if (!SceneLayers.IsKnown(layer))
        {
            throw new InvalidOperationException($"레이어는 {string.Join("·", SceneLayers.Order)} 중 하나다 ('{layer}')");
        }

        var o = new SceneObject { Id = id, Rig = rig, Layer = layer };
        Mutate(MotionChangeKind.Scene, () => scene.Objects.Add(o));
        return o;
    }

    public void RemoveSceneObject(string? sceneName, string id)
    {
        SceneAsset scene = RequireScene(sceneName);
        SceneObject o = RequireObject(scene, id);
        Mutate(MotionChangeKind.Scene, () => scene.Objects.Remove(o));
    }

    /// <summary>배치(위치·회전·배율) — 마야 채널 박스의 Translate/Rotate/Scale. 모션 키가 아니다.</summary>
    public void SetObjectPlacement(string? sceneName, string id, float x, float y, float rotationZ, float scaleX, float scaleY)
    {
        SceneAsset scene = RequireScene(sceneName);
        SceneObject o = RequireObject(scene, id);

        if (o.PositionX == x && o.PositionY == y && o.RotationZ == rotationZ && o.ScaleX == scaleX && o.ScaleY == scaleY)
        {
            return;
        }

        Mutate(MotionChangeKind.Scene, () =>
        {
            o.PositionX = x;
            o.PositionY = y;
            o.RotationZ = rotationZ;
            o.ScaleX = scaleX;
            o.ScaleY = scaleY;
        });
    }

    public void SetObjectLayer(string? sceneName, string id, string layer)
    {
        if (!SceneLayers.IsKnown(layer))
        {
            throw new InvalidOperationException($"레이어는 {string.Join("·", SceneLayers.Order)} 중 하나다 ('{layer}')");
        }

        SceneAsset scene = RequireScene(sceneName);
        SceneObject o = RequireObject(scene, id);

        if (o.Layer != layer)
        {
            Mutate(MotionChangeKind.Scene, () => o.Layer = layer);
        }
    }

    public void SetObjectVisible(string? sceneName, string id, bool visible)
    {
        SceneAsset scene = RequireScene(sceneName);
        SceneObject o = RequireObject(scene, id);

        if (o.Visible != visible)
        {
            Mutate(MotionChangeKind.Scene, () => o.Visible = visible);
        }
    }

    /// <summary>카메라(샷) — 보기 전용. zoom 1당 배율 0.05, pan은 리그 공간 px(코어 ShotIntentState).</summary>
    public void SetCamera(string? sceneName, float zoom, float panX, float panY)
    {
        SceneAsset scene = RequireScene(sceneName);
        CameraSettings camera = scene.Camera;

        if (camera.Zoom == zoom && camera.PanX == panX && camera.PanY == panY)
        {
            return;
        }

        Mutate(MotionChangeKind.Scene, () =>
        {
            camera.Zoom = zoom;
            camera.PanX = panX;
            camera.PanY = panY;
        });
    }

    /// <summary>미리보기 초상(문서 기준 상대 경로). null이면 자리표시.</summary>
    public void SetObjectPortrait(string? sceneName, string id, string? path)
    {
        SceneAsset scene = RequireScene(sceneName);
        SceneObject o = RequireObject(scene, id);

        if (o.Portrait != path)
        {
            Mutate(MotionChangeKind.Scene, () => o.Portrait = path);
        }
    }

    // ── 모션 ──────────────────────────────────────────────────────────

    public MotionAsset AddMotion(
        string name,
        string rig,
        float duration,
        MotionEnd end = MotionEnd.Restore,
        MotionChannelMode defaultMode = MotionChannelMode.Relative)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(rig);

        if (!MotionNames.IsValid(name))
        {
            throw new InvalidOperationException(MotionNames.RuleMessage(name));
        }

        if (Document.Find(name) is not null)
        {
            throw new InvalidOperationException($"모션 '{name}'이 이미 있다 — 이름은 하나뿐이어야 한다.");
        }

        var motion = new MotionAsset { Name = name, Rig = rig, Duration = duration, End = end, DefaultMode = defaultMode };
        Mutate(MotionChangeKind.Structure, () => Document.Motions.Add(motion));
        return motion;
    }

    public void RemoveMotion(string name)
    {
        MotionAsset motion = Require(name);
        Mutate(MotionChangeKind.Structure, () => Document.Motions.Remove(motion));
    }

    public void RenameMotion(string name, string newName)
    {
        ArgumentNullException.ThrowIfNull(newName);
        MotionAsset motion = Require(name);

        if (string.Equals(name, newName, StringComparison.Ordinal))
        {
            return;
        }

        if (!MotionNames.IsValid(newName))
        {
            throw new InvalidOperationException(MotionNames.RuleMessage(newName));
        }

        if (Document.Find(newName) is not null)
        {
            throw new InvalidOperationException($"모션 '{newName}'이 이미 있다.");
        }

        Mutate(MotionChangeKind.Metadata, () => motion.Name = newName);
    }

    /// <summary>
    /// 길이를 바꾼다. 키는 옮기지 않는다 — "끝 키를 길이로 옮기기"는 따로 있는 돕는 손이고,
    /// 어긋난 동안은 검증이 알린다.
    /// </summary>
    public void SetDuration(string name, float seconds)
    {
        MotionAsset motion = Require(name);
        Mutate(MotionChangeKind.Metadata, () => motion.Duration = seconds);
    }

    public void SetEnd(string name, MotionEnd end)
    {
        MotionAsset motion = Require(name);
        Mutate(MotionChangeKind.Metadata, () => motion.End = end);
    }

    public void SetDefaultMode(string name, MotionChannelMode mode)
    {
        MotionAsset motion = Require(name);
        Mutate(MotionChangeKind.Metadata, () => motion.DefaultMode = mode);
    }

    public void SetNotes(string name, string? notes)
    {
        MotionAsset motion = Require(name);
        Mutate(MotionChangeKind.Metadata, () => motion.Notes = string.IsNullOrEmpty(notes) ? null : notes);
    }

    // ── 채널 ──────────────────────────────────────────────────────────

    /// <param name="mode">null이면 모션의 기본 mode.</param>
    public ChannelAsset AddChannel(string name, string node, MotionChannelKind kind, MotionChannelMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        MotionAsset motion = Require(name);

        if (motion.FindChannel(node, kind) is not null)
        {
            throw new InvalidOperationException($"모션 '{name}'에 채널 {node}/{kind}가 이미 있다 — 같은 노드·종류는 하나뿐이다.");
        }

        var channel = new ChannelAsset { Node = node, Kind = kind, Mode = mode ?? motion.DefaultMode };
        Mutate(MotionChangeKind.Structure, () => motion.Channels.Add(channel));
        return channel;
    }

    public void RemoveChannel(string name, string node, MotionChannelKind kind)
    {
        MotionAsset motion = Require(name);
        ChannelAsset channel = RequireChannel(motion, node, kind);
        Mutate(MotionChangeKind.Structure, () => motion.Channels.Remove(channel));
    }

    public void SetChannelMode(string name, string node, MotionChannelKind kind, MotionChannelMode mode)
    {
        MotionAsset motion = Require(name);
        ChannelAsset channel = RequireChannel(motion, node, kind);
        Mutate(MotionChangeKind.Metadata, () => channel.Mode = mode);
    }

    // ── 키 ────────────────────────────────────────────────────────────

    /// <summary>성분의 키 목록을 통째로 바꾼다. 빈 목록은 "성분 없음". 시각 오름차순으로 정렬해 둔다.</summary>
    public void SetKeys(string name, string node, MotionChannelKind kind, Axis axis, IEnumerable<KeyAsset> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        MotionAsset motion = Require(name);
        ChannelAsset channel = RequireChannel(motion, node, kind);

        ComponentCurve? curve = Normalize(keys);

        Mutate(MotionChangeKind.Keys, () => channel.Set(axis, curve));
    }

    /// <summary>
    /// 여러 성분의 키 목록을 한 번에 바꾼다 — 드래그 한 번이 여러 커브를 건드려도 되돌리기는 하나다.
    /// 빈 목록은 "성분 없음". 가리킨 채널이 없으면 아무것도 바꾸지 않고 예외다.
    /// </summary>
    public void SetKeysMany(string name, IEnumerable<(CurveRef Curve, IEnumerable<KeyAsset> Keys)> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        MotionAsset motion = Require(name);
        var plan = new List<(ChannelAsset Channel, Axis Axis, ComponentCurve? Curve)>();

        foreach ((CurveRef curveRef, IEnumerable<KeyAsset> keys) in edits)
        {
            ChannelAsset channel = RequireChannel(motion, curveRef.Node, curveRef.Kind);
            plan.Add((channel, curveRef.Axis, Normalize(keys)));
        }

        if (plan.Count == 0)
        {
            return;
        }

        Mutate(MotionChangeKind.Keys, () =>
        {
            foreach ((ChannelAsset channel, Axis axis, ComponentCurve? curve) in plan)
            {
                channel.Set(axis, curve);
            }
        });
    }

    /// <summary>키들의 탄젠트 모드를 바꾼다. free가 아니면 탄젠트가 바로 다시 계산된다.</summary>
    public void SetTangentMode(string name, IEnumerable<KeyRef> keys, TangentMode mode)
    {
        ArgumentNullException.ThrowIfNull(keys);
        MotionAsset motion = Require(name);
        List<(ComponentCurve Curve, int Index)> targets = ResolveKeys(motion, keys);

        if (targets.Count == 0)
        {
            return;
        }

        Mutate(MotionChangeKind.Keys, () =>
        {
            foreach ((ComponentCurve curve, int index) in targets)
            {
                curve.Keys[index].Tangent = mode;
            }

            foreach (ComponentCurve curve in targets.Select(t => t.Curve).Distinct())
            {
                TangentMath.Recompute(curve.Keys);
            }
        });
    }

    /// <summary>in·out 탄젠트를 따로 놀게(끊기) 하거나 다시 붙인다. 붙이면 out이 in을 따른다. 모드는 free가 된다.</summary>
    public void SetBroken(string name, IEnumerable<KeyRef> keys, bool broken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        MotionAsset motion = Require(name);
        List<(ComponentCurve Curve, int Index)> targets = ResolveKeys(motion, keys);

        if (targets.Count == 0)
        {
            return;
        }

        Mutate(MotionChangeKind.Keys, () =>
        {
            foreach ((ComponentCurve curve, int index) in targets)
            {
                KeyAsset key = curve.Keys[index];
                key.Tangent = TangentMode.Free;
                key.Broken = broken;

                if (!broken)
                {
                    key.OutTangent = key.InTangent;
                }
            }
        });
    }

    /// <summary>핸들을 끈 결과. 모드는 free가 된다. 끊기지 않은 키는 양쪽이 같이 돈다 — 호출부가 같은 값을 넣는다.</summary>
    public void SetTangents(string name, KeyRef key, float inTangent, float outTangent)
    {
        MotionAsset motion = Require(name);
        KeyAsset target = key.Resolve(motion) ?? throw new KeyNotFoundException($"키 {key}가 없다.");

        Mutate(MotionChangeKind.Keys, () =>
        {
            target.Tangent = TangentMode.Free;
            target.InTangent = inTangent;
            target.OutTangent = outTangent;
            target.Broken = inTangent != outTangent;
        });
    }

    /// <summary>
    /// 포즈를 키로(Set Key·Auto Key). 채널이 없으면 모션 기본 mode로 만들고, 성분이 없으면 만들고, 같은 시각의 키는 바꾼다 —
    /// 전부 **한 번의 조작**이다. 돌려주는 것은 생긴 키들이다.
    /// </summary>
    public List<KeyRef> KeyComponents(
        string name, string node, MotionChannelKind kind, float time, IReadOnlyList<(Axis Axis, float Value)> values, TangentMode tangent)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(values);
        MotionAsset motion = Require(name);

        if (values.Count == 0)
        {
            return new List<KeyRef>();
        }

        var result = new List<KeyRef>();

        Mutate(MotionChangeKind.Keys, () =>
        {
            ChannelAsset? channel = motion.FindChannel(node, kind);

            if (channel is null)
            {
                channel = new ChannelAsset { Node = node, Kind = kind, Mode = motion.DefaultMode };
                motion.Channels.Add(channel);
            }

            foreach ((Axis axis, float value) in values)
            {
                ComponentCurve curve = channel.Get(axis) ?? new ComponentCurve();
                curve.Keys.RemoveAll(k => Math.Abs(k.T - time) <= MotionRules.Tolerance);
                curve.Keys.Add(new KeyAsset { T = time, V = value, Tangent = tangent });
                curve.Keys.Sort((a, b) => a.T.CompareTo(b.T));
                TangentMath.Recompute(curve.Keys);
                channel.Set(axis, curve);
                result.Add(new KeyRef(new CurveRef(node, kind, axis), curve.Keys.FindIndex(k => Math.Abs(k.T - time) <= MotionRules.Tolerance)));
            }
        });

        return result;
    }

    /// <summary>
    /// 포즈 하나를 여러 채널에 한 번의 조작으로 키 — 마야 "포즈 들고 스크럽 뒤 S". 없는 채널·성분은 모션 기본 mode로 만든다.
    /// </summary>
    public List<KeyRef> KeyPose(
        string name, float time, IEnumerable<(string Node, MotionChannelKind Kind, IReadOnlyList<(Axis Axis, float Value)> Values)> items, TangentMode tangent)
    {
        ArgumentNullException.ThrowIfNull(items);
        MotionAsset motion = Require(name);
        var list = items.Where(i => i.Values.Count > 0).ToList();
        var result = new List<KeyRef>();

        if (list.Count == 0)
        {
            return result;
        }

        Mutate(MotionChangeKind.Keys, () =>
        {
            foreach ((string node, MotionChannelKind kind, IReadOnlyList<(Axis Axis, float Value)> values) in list)
            {
                ChannelAsset? channel = motion.FindChannel(node, kind);

                if (channel is null)
                {
                    channel = new ChannelAsset { Node = node, Kind = kind, Mode = motion.DefaultMode };
                    motion.Channels.Add(channel);
                }

                foreach ((Axis axis, float value) in values)
                {
                    ComponentCurve curve = channel.Get(axis) ?? new ComponentCurve();
                    curve.Keys.RemoveAll(k => Math.Abs(k.T - time) <= MotionRules.Tolerance);
                    curve.Keys.Add(new KeyAsset { T = time, V = value, Tangent = tangent });
                    curve.Keys.Sort((a, b) => a.T.CompareTo(b.T));
                    TangentMath.Recompute(curve.Keys);
                    channel.Set(axis, curve);
                    result.Add(new KeyRef(new CurveRef(node, kind, axis), curve.Keys.FindIndex(k => Math.Abs(k.T - time) <= MotionRules.Tolerance)));
                }
            }
        });

        return result;
    }

    /// <summary>선택 키의 값을 바꾼다(한 번의 조작). 시각은 그대로라 선택이 그대로 유효하다.</summary>
    public void SetKeyValues(string name, IEnumerable<KeyRef> keys, Func<KeyAsset, float> map)
    {
        ArgumentNullException.ThrowIfNull(keys);
        MotionAsset motion = Require(name);
        SetKeysMany(name, KeyOps.MapValues(motion, keys.ToList(), map));
    }

    /// <summary>
    /// 클립보드를 현재 시각에 붙인다(merge — 같은 시각의 키는 바뀐다). 채널·성분이 없으면 모션 기본 mode로 만든다. 한 번의 조작.
    /// 돌려주는 것은 붙은 키들이다.
    /// </summary>
    public List<KeyRef> PasteKeys(string name, KeyClipboard clipboard, float atTime, Func<float, float>? snap = null)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        MotionAsset motion = Require(name);
        float shift = atTime - clipboard.Origin;
        var result = new List<KeyRef>();

        if (clipboard.Count == 0)
        {
            return result;
        }

        Mutate(MotionChangeKind.Keys, () =>
        {
            foreach ((CurveRef curveRef, IReadOnlyList<KeyAsset> keys) in clipboard.Items)
            {
                ChannelAsset? channel = motion.FindChannel(curveRef.Node, curveRef.Kind);

                if (channel is null)
                {
                    channel = new ChannelAsset { Node = curveRef.Node, Kind = curveRef.Kind, Mode = motion.DefaultMode };
                    motion.Channels.Add(channel);
                }

                ComponentCurve curve = channel.Get(curveRef.Axis) ?? new ComponentCurve();
                var pastedTimes = new List<float>();

                foreach (KeyAsset key in keys)
                {
                    float t = Math.Max(0f, snap is null ? key.T + shift : snap(key.T + shift));
                    curve.Keys.RemoveAll(k => Math.Abs(k.T - t) <= MotionRules.Tolerance);
                    curve.Keys.Add(new KeyAsset
                    {
                        T = t, V = key.V, InTangent = key.InTangent, OutTangent = key.OutTangent, Tangent = key.Tangent, Broken = key.Broken,
                    });
                    pastedTimes.Add(t);
                }

                curve.Keys.Sort((a, b) => a.T.CompareTo(b.T));
                TangentMath.Recompute(curve.Keys);
                channel.Set(curveRef.Axis, curve);

                foreach (float t in pastedTimes)
                {
                    int index = curve.Keys.FindIndex(k => Math.Abs(k.T - t) <= MotionRules.Tolerance);

                    if (index >= 0)
                    {
                        result.Add(new KeyRef(curveRef, index));
                    }
                }
            }
        });

        return result;
    }

    /// <summary>선택 키를 delta초 옮긴다(한 번의 조작). 돌려주는 것은 옮긴 뒤의 (커브, 시각) — 선택을 다시 푸는 데 쓴다.</summary>
    public List<(CurveRef Curve, float T)> MoveKeys(string name, IEnumerable<KeyRef> keys, float deltaSeconds, Func<float, float>? snap = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        MotionAsset motion = Require(name);
        List<KeyRef> list = keys.ToList();
        var result = KeyOps.ResultingTimes(motion, list, t => t + deltaSeconds, snap);
        SetKeysMany(name, KeyOps.Move(motion, list, deltaSeconds, snap));
        return result;
    }

    /// <summary>pivot 기준으로 선택 키의 시각을 factor배(한 번의 조작).</summary>
    public List<(CurveRef Curve, float T)> ScaleKeys(string name, IEnumerable<KeyRef> keys, float pivotSeconds, float factor, Func<float, float>? snap = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        MotionAsset motion = Require(name);
        List<KeyRef> list = keys.ToList();
        var result = KeyOps.ResultingTimes(motion, list, t => pivotSeconds + (t - pivotSeconds) * factor, snap);
        SetKeysMany(name, KeyOps.Scale(motion, list, pivotSeconds, factor, snap));
        return result;
    }

    /// <summary>선택 키의 사본을 offset초 자리에 둔다(한 번의 조작). 돌려주는 것은 사본의 (커브, 시각).</summary>
    public List<(CurveRef Curve, float T)> DuplicateKeys(string name, IEnumerable<KeyRef> keys, float offsetSeconds, Func<float, float>? snap = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        MotionAsset motion = Require(name);
        List<KeyRef> list = keys.ToList();
        var result = KeyOps.ResultingTimes(motion, list, t => t + offsetSeconds, snap);
        SetKeysMany(name, KeyOps.Duplicate(motion, list, offsetSeconds, snap));
        return result;
    }

    /// <summary>마야 Add/Remove Inbetween — <paramref name="atSeconds"/> 뒤의 키를 전부 민다(한 번의 조작). 바뀐 것이 없으면 false.</summary>
    public bool ShiftKeysAfter(string name, float atSeconds, float deltaSeconds)
    {
        MotionAsset motion = Require(name);
        var edits = TimeSliderOps.Inbetween(motion, atSeconds, deltaSeconds);

        if (edits.Count == 0)
        {
            return false;
        }

        SetKeysMany(name, edits);
        return true;
    }

    /// <summary>마야 타임 슬라이더 Clear Key — 그 시각의 키를 모든 커브에서 지운다(한 번의 조작). 없으면 false.</summary>
    public bool RemoveKeysAt(string name, double seconds)
    {
        MotionAsset motion = Require(name);
        List<KeyRef> keys = TimeSliderOps.KeysAt(motion, seconds);

        if (keys.Count == 0)
        {
            return false;
        }

        RemoveKeys(name, keys);
        return true;
    }

    /// <summary>키들을 지운다. 성분이 비면 "성분 없음"이 된다. 한 번의 조작이다.</summary>
    public void RemoveKeys(string name, IEnumerable<KeyRef> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        MotionAsset motion = Require(name);
        var byCurve = new Dictionary<CurveRef, List<int>>();

        foreach (KeyRef key in keys)
        {
            if (key.Resolve(motion) is null)
            {
                continue;
            }

            if (!byCurve.TryGetValue(key.Curve, out List<int>? indices))
            {
                indices = new List<int>();
                byCurve[key.Curve] = indices;
            }

            indices.Add(key.Index);
        }

        if (byCurve.Count == 0)
        {
            return;
        }

        var edits = new List<(CurveRef, IEnumerable<KeyAsset>)>();

        foreach ((CurveRef curveRef, List<int> indices) in byCurve)
        {
            ComponentCurve curve = curveRef.Resolve(motion)!;
            var remaining = new HashSet<int>(indices);
            edits.Add((curveRef, curve.Keys.Where((_, i) => !remaining.Contains(i)).ToList()));
        }

        SetKeysMany(name, edits);
    }

    private static ComponentCurve? Normalize(IEnumerable<KeyAsset> keys)
    {
        List<KeyAsset> sorted = keys.Select(Clone).OrderBy(k => k.T).ToList();

        if (sorted.Count == 0)
        {
            return null;
        }

        TangentMath.Recompute(sorted);
        return new ComponentCurve { Keys = sorted };
    }

    private static List<(ComponentCurve Curve, int Index)> ResolveKeys(MotionAsset motion, IEnumerable<KeyRef> keys)
    {
        var targets = new List<(ComponentCurve Curve, int Index)>();

        foreach (KeyRef key in keys.Distinct())
        {
            ComponentCurve? curve = key.Curve.Resolve(motion);

            if (curve is not null && key.Index >= 0 && key.Index < curve.Keys.Count)
            {
                targets.Add((curve, key.Index));
            }
        }

        return targets;
    }

    /// <summary>키를 더한다. 같은 시각(허용 오차 안)에 키가 있으면 그 키를 대신한다 — 한 시각에 키는 하나다.</summary>
    public KeyAsset AddKey(string name, string node, MotionChannelKind kind, Axis axis, KeyAsset key)
    {
        ArgumentNullException.ThrowIfNull(key);
        MotionAsset motion = Require(name);
        ChannelAsset channel = RequireChannel(motion, node, kind);
        KeyAsset copy = Clone(key);

        Mutate(MotionChangeKind.Keys, () =>
        {
            ComponentCurve curve = channel.Get(axis) ?? new ComponentCurve();
            curve.Keys.RemoveAll(k => Math.Abs(k.T - copy.T) <= MotionRules.Tolerance);
            curve.Keys.Add(copy);
            curve.Keys.Sort((a, b) => a.T.CompareTo(b.T));
            TangentMath.Recompute(curve.Keys);
            channel.Set(axis, curve);
        });

        return copy;
    }

    public void RemoveKey(string name, string node, MotionChannelKind kind, Axis axis, int index)
    {
        MotionAsset motion = Require(name);
        ChannelAsset channel = RequireChannel(motion, node, kind);
        ComponentCurve curve = RequireCurve(channel, axis, index);

        Mutate(MotionChangeKind.Keys, () =>
        {
            curve.Keys.RemoveAt(index);

            if (curve.Keys.Count == 0)
            {
                channel.Set(axis, null);
            }
            else
            {
                TangentMath.Recompute(curve.Keys);
            }
        });
    }

    /// <summary>키 하나의 값을 바꾼다. 시각이 바뀌면 다시 정렬하므로 index는 호출 뒤 유효하지 않을 수 있다.</summary>
    public void UpdateKey(string name, string node, MotionChannelKind kind, Axis axis, int index, KeyAsset value)
    {
        ArgumentNullException.ThrowIfNull(value);
        MotionAsset motion = Require(name);
        ChannelAsset channel = RequireChannel(motion, node, kind);
        ComponentCurve curve = RequireCurve(channel, axis, index);
        KeyAsset copy = Clone(value);

        Mutate(MotionChangeKind.Keys, () =>
        {
            curve.Keys[index] = copy;
            curve.Keys.Sort((a, b) => a.T.CompareTo(b.T));
            TangentMath.Recompute(curve.Keys);
        });
    }

    // ── 가져오기 ──────────────────────────────────────────────────────

    /// <summary>
    /// <c>motions/1</c> 클립들을 저작본으로 올린다(탄젠트는 free). 이름이 겹치면 <paramref name="replace"/>가
    /// 참일 때만 바꿔 넣고, 아니면 건너뛴다. 돌려주는 것은 들어온 이름들이다.
    /// </summary>
    public IReadOnlyList<string> ImportClips(IEnumerable<MotionClip> clips, bool replace)
    {
        ArgumentNullException.ThrowIfNull(clips);

        var incoming = clips.Select(MotionAssetCodec.FromClip).ToList();
        var imported = new List<string>();
        var plan = new List<(int Index, MotionAsset Asset)>();

        foreach (MotionAsset asset in incoming)
        {
            int existing = Document.Motions.FindIndex(m => string.Equals(m.Name, asset.Name, StringComparison.Ordinal));

            if (existing < 0 || replace)
            {
                plan.Add((existing, asset));
                imported.Add(asset.Name);
            }
        }

        // 바뀔 것이 없으면 조작도 없다 — Revision과 되돌리기 기록에 빈 항목을 남기지 않는다.
        if (plan.Count == 0)
        {
            return imported;
        }

        Mutate(MotionChangeKind.Structure, () =>
        {
            foreach ((int index, MotionAsset asset) in plan)
            {
                if (index < 0)
                {
                    Document.Motions.Add(asset);
                }
                else
                {
                    Document.Motions[index] = asset;
                }
            }
        });

        return imported;
    }

    // ── 되돌리기 ──────────────────────────────────────────────────────

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Add(MotionDocumentJson.Write(Document));
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Add(MotionDocumentJson.Write(Document));
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
    }

    /// <summary>파일에서 새로 읽었을 때처럼, 기록을 버리고 통째로 갈아 끼운다.</summary>
    public void Replace(MotionDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        _undo.Clear();
        _redo.Clear();
        Raise(MotionChangeKind.Structure);
    }

    private void Restore(string snapshot)
    {
        Document = MotionDocumentJson.Read(snapshot);
        Raise(MotionChangeKind.Structure);
    }

    private void Mutate(MotionChangeKind kind, Action change)
    {
        _undo.Add(MotionDocumentJson.Write(Document));

        if (_undo.Count > MaxHistory)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        change();
        Raise(kind);
    }

    private void Raise(MotionChangeKind kind)
    {
        Revision++;
        Changed?.Invoke(this, new MotionChangedEventArgs(kind));
    }

    private MotionAsset Require(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Document.Find(name) ?? throw new KeyNotFoundException($"모션 '{name}'이 없다.");
    }

    private static ChannelAsset RequireChannel(MotionAsset motion, string node, MotionChannelKind kind)
    {
        ArgumentNullException.ThrowIfNull(node);
        return motion.FindChannel(node, kind) ?? throw new KeyNotFoundException($"모션 '{motion.Name}'에 채널 {node}/{kind}가 없다.");
    }

    private static ComponentCurve RequireCurve(ChannelAsset channel, Axis axis, int index)
    {
        ComponentCurve curve = channel.Get(axis) ?? throw new KeyNotFoundException($"채널 {channel.Node}/{channel.Kind}에 {axis} 성분이 없다.");

        if (index < 0 || index >= curve.Keys.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"키가 {curve.Keys.Count}개다.");
        }

        return curve;
    }

    // 호출자의 객체를 문서에 꽂지 않는다 — 문서 밖에서 고치면 되돌리기 스냅샷이 거짓이 된다.
    private static KeyAsset Clone(KeyAsset key) => new()
    {
        T = key.T,
        V = key.V,
        InTangent = key.InTangent,
        OutTangent = key.OutTangent,
        Tangent = key.Tangent,
        Broken = key.Broken,
    };
}

public enum MotionChangeKind
{
    /// <summary>모션·채널이 생기거나 사라졌다. 목록을 다시 만든다.</summary>
    Structure,

    /// <summary>이름·길이·end·mode·메모. 목록은 그대로, 글자만 다시 쓴다.</summary>
    Metadata,

    /// <summary>씬·오브젝트·배치가 바뀌었다. 뷰포트가 무대를 다시 세운다.</summary>
    Scene,

    /// <summary>키 값. 그래프·뷰포트만 다시 그린다.</summary>
    Keys,

    /// <summary>리그 덤프·기준 해상도·미리보기 설정.</summary>
    Document,
}

public sealed class MotionChangedEventArgs : EventArgs
{
    public MotionChangedEventArgs(MotionChangeKind kind)
    {
        Kind = kind;
    }

    public MotionChangeKind Kind { get; }
}
