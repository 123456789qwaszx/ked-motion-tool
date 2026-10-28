using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Preview;

/// <summary>
/// 씬 → 트리. 오브젝트마다 리그를 슬롯 접두사로 세우고(코어 <c>RigSchemaLoader.AddRigTo</c> — 리듀서의 slot 폴드와 같은 함수),
/// 배치 값을 배치 축 노드에 넣고, 초상 사이징을 입힌다. 모션은 <see cref="Pose"/>가 고른 오브젝트 하나에 얹는다.
/// </summary>
public static class ScenePreview
{
    /// <summary>
    /// 씬 전체의 시작 상태. 덤프에 없는 rigKind의 오브젝트는 건너뛴다(<paramref name="missing"/>에 남긴다).
    /// 루트 공간은 덤프가 캡처된 공간이다(스트레치 노드 크기의 기준).
    /// </summary>
    public const string DepthYNode = SetDepthStageReduction.DepthYNodeId;
    public const string DepthScaleNode = SetDepthStageReduction.DepthScaleNodeId;

    /// <param name="depth">presets/depth.json. 있으면 레이어 라벨을 깊이(DepthY·DepthScale)로 — 런타임 size 폴드와 같은 함수(focus 보존 bust, 캐릭터별 오프셋 없이).</param>
    /// <param name="fallenBack">부모 노드가 그 리그에 없어 부모 루트에 붙인 오브젝트들(알림용).</param>
    /// <param name="focus">presets/focus.json. 있으면 배역(<see cref="SceneObject.Character"/>)별 focus 오프셋이 깊이 폴드에 들어간다.</param>
    public static RectNodeTree Build(RigSchemaFile rig, SceneAsset scene, Func<SceneObject, float>? aspectOf = null, List<SceneObject>? missing = null, DepthTuningFile? depth = null, List<SceneObject>? fallenBack = null, FocusTuningFile? focus = null)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(scene);

        var space = new RectSpace(rig.Dto.capturedUnderParentSize.ToVec2(), Vec2.Half);
        var state = new StageState(space);

        // DrawOrder는 부모가 자식보다 먼저다 — 자식은 이미 선 부모의 노드 밑에 붙는다(코어 AddRigTo의 attachParentKey).
        foreach (SceneObject o in scene.DrawOrder())
        {
            string? attach = null;

            if (o.Parent is not null)
            {
                SceneObject? parent = scene.Find(o.Parent);

                if (parent is null || !state.Nodes.Contains(RootKeyOf(parent)))
                {
                    missing?.Add(o);
                    continue;
                }

                attach = o.ParentNode is null ? RootKeyOf(parent) : parent.KeyOf(o.ParentNode);

                if (!state.Nodes.Contains(attach))
                {
                    fallenBack?.Add(o);
                    attach = RootKeyOf(parent);
                }
            }

            if (o.IsEmpty)
            {
                state.Nodes.Add(RootKeyOf(o), attach!, EmptyState(o));
                continue;
            }

            if (!rig.TryGetRig(o.Rig, out RigSchemaRigDto dto))
            {
                missing?.Add(o);
                continue;
            }

            RigSchemaLoader.AddRigTo(state.Nodes, dto, o.Prefix, attach!);
            state.RegisterSlot(o.Id);
            ApplyPlacement(state.Nodes, o);
            PortraitLayout.ApplySizing(state.Nodes, aspectOf?.Invoke(o) ?? PortraitLayout.PlaceholderAspect, o.Prefix);

            // 깊이는 최상위에만 — 자식은 부모 사슬을 따라 같이 깊어진다.
            if (o.Parent is null)
            {
                ApplyDepth(state, o, depth, focus);
            }
        }

        return state.Nodes;
    }

    /// <summary>빈 오브젝트의 노드 하나 — 가운데 앵커·피벗, 크기 0, 배치 값 그대로(유니티 Create Empty).</summary>
    public static RectNodeState EmptyState(SceneObject o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new RectNodeState(
            anchoredPosition: new Vec2(o.PositionX, o.PositionY),
            anchorMin: Vec2.Half,
            anchorMax: Vec2.Half,
            pivot: Vec2.Half,
            sizeDelta: Vec2.Zero,
            localScale: new Vec3(o.ScaleX, o.ScaleY, 1f),
            localEulerAngles: new Vec3(0f, 0f, o.RotationZ));
    }

    /// <summary>배치 값을 배치 축 노드에. 그 노드가 없는 리그(배경 등)면 루트 노드에 — 그래서 어느 오브젝트든 옮길 수 있다.</summary>
    public static void ApplyPlacement(RectNodeTree tree, SceneObject o)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(o);

        string root = RootKeyOf(o);
        string position = tree.Contains(o.KeyOf(PlacementNodes.Position)) ? o.KeyOf(PlacementNodes.Position) : root;
        string rotation = tree.Contains(o.KeyOf(PlacementNodes.Rotation)) ? o.KeyOf(PlacementNodes.Rotation) : root;
        string scale = tree.Contains(o.KeyOf(PlacementNodes.Scale)) ? o.KeyOf(PlacementNodes.Scale) : root;

        if (tree.Contains(position))
        {
            tree.SetState(position, tree.GetState(position).WithAnchoredPosition(new Vec2(o.PositionX, o.PositionY)));
        }

        if (tree.Contains(rotation))
        {
            RectNodeState state = tree.GetState(rotation);
            tree.SetState(rotation, state.WithLocalEuler(new Vec3(state.LocalEulerAngles.X, state.LocalEulerAngles.Y, o.RotationZ)));
        }

        if (tree.Contains(scale))
        {
            RectNodeState state = tree.GetState(scale);
            tree.SetState(scale, state.WithLocalScale(new Vec3(o.ScaleX, o.ScaleY, state.LocalScale.Z)));
        }
    }

    /// <summary>
    /// 레이어 → 깊이(DepthY 위치·DepthScale 배율). 런타임 size 폴드와 **같은 함수**(<see cref="SetDepthStageReduction"/>, focus 보존 bust)를
    /// 지나므로 보이는 깊이 = 정지 프레임의 깊이다. 커브가 없거나 리그에 깊이 축이 없으면 건너뛴다(false).
    /// </summary>
    public static bool ApplyDepth(StageState state, SceneObject o, DepthTuningFile? depth, FocusTuningFile? focus = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(o);

        if (depth is null || !SceneLayers.IsKnown(o.Layer))
        {
            return false;
        }

        if (!SetDepthStageReduction.TryReduce(state, o.Id, o.Character ?? "", o.Layer, "bust", depth.Level, focus?.Body, out StageNodeClaim depthY, out StageNodeClaim depthScale, out _))
        {
            return false;
        }

        state.Apply(depthY);
        state.Apply(depthScale);
        return true;
    }

    /// <summary>
    /// 부모를 바꿀 때 월드 위치를 지키는 배치 값(유니티 "Set Parent" 기본). 지금 트리에서 오브젝트 루트의 월드 점을 재고,
    /// 새 부모 밑에 같은 배치로 세운 트리에서 그 점이 어디로 가는지 본 뒤, 차이를 배치 노드의 부모 공간으로 되돌려 더한다.
    /// 회전·배율은 지키지 않는다(위치만). 리그가 없어 못 재면 지금 값 그대로.
    /// </summary>
    public static (float X, float Y) PlacementKeepingWorld(RigSchemaFile rig, SceneAsset scene, string id, string? newParent, string? newParentNode, DepthTuningFile? depth = null, FocusTuningFile? focus = null)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(scene);
        SceneObject o = scene.Find(id) ?? throw new KeyNotFoundException($"오브젝트 '{id}'가 없다.");
        (float X, float Y) current = (o.PositionX, o.PositionY);
        string root = RootKeyOf(o);

        RectNodeTree before = Build(rig, scene, null, null, depth, null, focus);

        if (!before.Contains(root))
        {
            return current;
        }

        // 지키는 점 = 배치 위치가 움직이는 노드(CharSlot_Track, 없으면 루트)의 원점. 리그 루트는 배치로 움직이지 않는다.
        string positionKey = before.Contains(o.KeyOf(PlacementNodes.Position)) ? o.KeyOf(PlacementNodes.Position) : root;
        Vec3 world = before.TransformPoint(positionKey, Vec3.Zero);

        // 같은 씬을 새 부모로 흉내 낸다 — 문서는 건드리지 않는다.
        var trial = new SceneAsset { Name = scene.Name, Camera = scene.Camera };
        SceneObject? parent = newParent is null ? null : scene.Find(newParent);

        foreach (SceneObject original in scene.Objects)
        {
            trial.Objects.Add(original.Id == id
                ? new SceneObject
                {
                    Id = original.Id, Rig = original.Rig, Stage = parent?.Stage ?? original.Stage, Layer = parent?.Layer ?? original.Layer,
                    PositionX = original.PositionX, PositionY = original.PositionY, RotationZ = original.RotationZ, ScaleX = original.ScaleX, ScaleY = original.ScaleY,
                    Visible = original.Visible, Character = original.Character, Parent = parent?.Id, ParentNode = parent is null ? null : newParentNode,
                }
                : original);
        }

        RectNodeTree after = Build(rig, trial, null, null, depth, null, focus);

        if (!after.Contains(positionKey))
        {
            return current;
        }

        // 배치 값이 사는 공간 = 배치 위치 노드의 부모 로컬. 월드 차이를 그 공간으로 가져온다.
        string? parentKey = after.GetParentKey(positionKey);
        Vec3 nowWorld = after.TransformPoint(positionKey, Vec3.Zero);
        Vec2 delta = parentKey is null
            ? (world - nowWorld).XY
            : (after.InverseTransformPoint(parentKey, world) - after.InverseTransformPoint(parentKey, nowWorld)).XY;
        return (current.X + delta.X, current.Y + delta.Y);
    }

    /// <summary>모션을 오브젝트 하나에 얹은 시각 t의 포즈. 채널의 노드가 그 리그에 없으면 그 채널은 건너뛴다.</summary>
    public static RectNodeTree Pose(RectNodeTree template, SceneObject target, MotionAsset motion, double seconds, Func<Graph.CurveRef, bool>? isMuted = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        return MotionPreview.Pose(template, motion, seconds, isMuted, target.Prefix);
    }

    /// <summary>
    /// 이 오브젝트에 모션을 입혀 볼 수 있나 — rigKind가 같거나, 채널의 노드가 전부 그 리그에 있으면(공통 스키마면 공유) 된다.
    /// </summary>
    public static bool CanWear(RigSchemaFile rig, SceneObject o, MotionAsset motion)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(motion);

        if (o.IsEmpty)
        {
            return false;
        }

        if (string.Equals(o.Rig, motion.Rig, StringComparison.Ordinal))
        {
            return true;
        }

        // 채널이 없으면 공유할 것도 없다 — 리그가 다르면 안 맞는 것으로 본다.
        return motion.Channels.Count > 0
            && rig.NodeIds.TryGetValue(o.Rig, out ICollection<string>? nodes)
            && motion.Channels.All(c => nodes.Contains(c.Node));
    }

    /// <summary>오브젝트의 그림 노드 키(초상). 리그에 없으면 null — 그때는 루트 rect를 윤곽으로 그린다.</summary>
    public static string? ImageKeyOf(RectNodeTree tree, SceneObject o)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(o);
        string key = o.KeyOf(PortraitLayout.ImageNodeId);
        return tree.Contains(key) ? key : null;
    }

    public static string RootKeyOf(SceneObject o) => o.KeyOf(RigSchemaLoader.RootKey);
}
