using Ked.Presentation.Core;

namespace Mt.Authoring.Preview;

/// <summary>루트 공간(가운데 원점·y 위)의 네 모서리. 회전·배율이 섞여도 그대로 든다 — 축 정렬 경계가 아니다.</summary>
public readonly record struct Quad(Vec2 BottomLeft, Vec2 BottomRight, Vec2 TopRight, Vec2 TopLeft)
{
    public Vec2 Center => (BottomLeft + TopRight) * 0.5f;
}

/// <summary>
/// 초상 한 장이 무대 어디에 어떻게 서는가 — VnTool <c>StageSceneComposer.PlaceCorePortraits</c>의 자리이되,
/// 상태 폴드 대신 포즈(<see cref="MotionPreview"/>)를 먹는다.
///
/// 사이징은 런타임 <c>HeightFitPreserveAspect</c>와 같다: <c>sizeDelta = (부모 rect 높이 × 종횡비, 0)</c>
/// (코어 <see cref="PortraitSizingReduction"/>). 노드 rect는 스트레치 앵커라 그보다 넓지만, Unity Image의
/// <c>preserveAspect</c>가 스프라이트를 rect 안에 종횡비대로 가운데 맞추므로 **보이는 것**은 높이 맞춤이다.
/// <see cref="VisualQuad"/>가 그 보이는 사각형이다.
/// </summary>
public static class PortraitLayout
{
    public const string ImageNodeId = PortraitSizingStageReduction.ImageNodeId;
    public const string ParentNodeId = PortraitSizingStageReduction.ParentNodeId;

    /// <summary>초상이 없을 때 쓰는 자리표시 종횡비(실측 덤프 harin/a/01: 1647×2048).</summary>
    public const float PlaceholderAspect = 1647f / 2048f;

    /// <summary>런타임이 스프라이트를 올릴 때 하는 사이징을 트리에 적용한다. 덤프에 그 노드가 없으면 아무것도 안 한다.</summary>
    public static void ApplySizing(RectNodeTree tree, float aspect, string keyPrefix = "")
    {
        ArgumentNullException.ThrowIfNull(tree);
        string imageKey = keyPrefix + ImageNodeId;
        string parentKey = keyPrefix + ParentNodeId;

        if (!tree.Contains(imageKey) || !tree.Contains(parentKey))
        {
            return;
        }

        float parentHeight = tree.GetRectSize(parentKey).Y;
        StageNodeClaim claim = PortraitSizingReduction.Reduce(imageKey, parentHeight, aspect);
        tree.SetState(imageKey, tree.GetState(imageKey).WithSizeDelta(claim.Value.XY));
    }

    /// <summary>노드 rect의 네 모서리(루트 공간). 원점은 pivot.</summary>
    public static Quad RectQuad(RectNodeTree tree, string key)
    {
        ArgumentNullException.ThrowIfNull(tree);
        RectNodeState state = tree.GetState(key);
        Vec2 size = tree.GetRectSize(key);
        float left = -state.Pivot.X * size.X;
        float bottom = -state.Pivot.Y * size.Y;
        return Transform(tree, key, left, bottom, size.X, size.Y);
    }

    /// <summary>스프라이트가 실제로 보이는 사각형 — rect 안에 종횡비대로 가운데 맞춘 것(Unity Image.preserveAspect).</summary>
    public static Quad VisualQuad(RectNodeTree tree, string key, float aspect)
    {
        ArgumentNullException.ThrowIfNull(tree);

        if (!(aspect > 0f) || float.IsInfinity(aspect))
        {
            throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "종횡비는 양수여야 한다");
        }

        RectNodeState state = tree.GetState(key);
        Vec2 size = tree.GetRectSize(key);

        float width = Math.Min(size.X, size.Y * aspect);
        float height = width / aspect;
        float centerX = (0.5f - state.Pivot.X) * size.X;
        float centerY = (0.5f - state.Pivot.Y) * size.Y;

        return Transform(tree, key, centerX - width / 2f, centerY - height / 2f, width, height);
    }

    /// <summary>노드의 pivot(루트 공간)과 그 로컬 x·y 축 끝 — 선택 노드의 축 표시에 쓴다.</summary>
    public static (Vec2 Origin, Vec2 XEnd, Vec2 YEnd) Axes(RectNodeTree tree, string key, float length)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return (
            tree.TransformPoint(key, Vec3.Zero).XY,
            tree.TransformPoint(key, new Vec3(length, 0f, 0f)).XY,
            tree.TransformPoint(key, new Vec3(0f, length, 0f)).XY);
    }

    /// <summary>루트 공간(가운데 원점·y 위) → 무대 캔버스(좌상 원점·y 아래, 무대 px).</summary>
    public static Vec2 ToCanvas(Vec2 root, Vec2 stageSize)
        => new(stageSize.X / 2f + root.X, stageSize.Y / 2f - root.Y);

    private static Quad Transform(RectNodeTree tree, string key, float left, float bottom, float width, float height)
    {
        return new Quad(
            tree.TransformPoint(key, new Vec3(left, bottom, 0f)).XY,
            tree.TransformPoint(key, new Vec3(left + width, bottom, 0f)).XY,
            tree.TransformPoint(key, new Vec3(left + width, bottom + height, 0f)).XY,
            tree.TransformPoint(key, new Vec3(left, bottom + height, 0f)).XY);
    }
}
