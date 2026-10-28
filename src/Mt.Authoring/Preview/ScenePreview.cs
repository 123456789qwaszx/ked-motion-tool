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
    /// <param name="focus">presets/focus.json. 있으면 그 표의 focus 오프셋(base + bust, 배역 키 없이)이 깊이·place 폴드에 들어간다.</param>
    /// <param name="stage">
    /// 무대 뼈대(<c>PresentataionUIRoot</c>, rigKind "stage"). 있으면 <c>stage/</c> 접두사로 먼저 서고 오브젝트는
    /// <c>Stage{NN}Depth_{Layer}_Content</c> 밑에 붙으며, 카메라(zoom·pan)와 층 패럴랙스가 뼈대 노드에 들어간다. 없으면 루트 직속(좌표는 같다 — 뼈대는 항등).
    /// </param>
    public static RectNodeTree Build(RigSchemaFile rig, SceneAsset scene, Func<SceneObject, float>? aspectOf = null, List<SceneObject>? missing = null, DepthTuningFile? depth = null, FocusTuningFile? focus = null, RigSchemaRigDto? stage = null)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(scene);

        var space = new RectSpace(rig.Dto.capturedUnderParentSize.ToVec2(), Vec2.Half);
        var state = new StageState(space);

        if (stage is not null)
        {
            RigSchemaLoader.AddRigTo(state.Nodes, stage, StageNodes.Prefix);
            StageShotResponse.Apply(state.Nodes, scene.Camera);
        }

        foreach (SceneObject o in scene.DrawOrder())
        {
            string? attach = null;

            if (stage is not null)
            {
                // 오브젝트는 그 무대·층의 Content에 선다. 런타임 GetDepthContent(stage, layer)와 같은 자리. 모르는 라벨은 루트 직속.
                string content = StageNodes.KeyOf(StageNodes.ContentOf(o.Stage, o.Layer));
                attach = state.Nodes.Contains(content) ? content : null;
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

            // 순서는 런타임과 같이 size(깊이) → place.
            ApplyDepth(state, o, depth, focus);
            ApplyPlace(state, o, focus);
        }

        return state.Nodes;
    }

    /// <summary>
    /// 자리(place) — focus 프리셋 점이 화면 지점에 오도록 <c>CharSlot_Track_Focus</c>를 푼다. 코어 <see cref="PlaceFocusStageReduction"/> 그대로.
    /// place가 없거나 리그에 그 축이 없으면 건너뛴다(false). 깊이 뒤에 부른다(런타임 순서: size → place; 그래야 DepthY가 게임과 같다).
    /// </summary>
    public static bool ApplyPlace(StageState state, SceneObject o, FocusTuningFile? focus)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(o);

        if (string.IsNullOrEmpty(o.Place))
        {
            return false;
        }

        if (!PlaceFocusStageReduction.TryReduce(state, o.Id, "", o.PlaceFocus ?? SceneObject.DefaultPlaceFocus, o.Place, focus?.Body, out StageNodeClaim claim, out _))
        {
            return false;
        }

        state.Apply(claim);
        return true;
    }

    /// <summary>화면 지점 이름들 — 코어 <see cref="ScreenPointRatios"/>가 아는 것.</summary>
    public static readonly IReadOnlyList<string> ScreenPoints = new[] { "center", "left", "right", "top", "bottom", "tl", "tr", "bl", "br" };

    /// <summary>focus 프리셋 이름들 — 코어 <see cref="FocusPresetName"/>이 아는 것.</summary>
    public static readonly IReadOnlyList<string> FocusPresets = new[] { "bust", "face", "body", "feet", "faceAura", "handLeft", "handRight" };

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

        if (!SetDepthStageReduction.TryReduce(state, o.Id, "", o.Layer, "bust", depth.Level, focus?.Body, out StageNodeClaim depthY, out StageNodeClaim depthScale, out _))
        {
            return false;
        }

        state.Apply(depthY);
        state.Apply(depthScale);
        return true;
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
