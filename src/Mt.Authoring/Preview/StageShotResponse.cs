using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Preview;

/// <summary>
/// 카메라(샷)를 무대 뼈대에 입힌다 — 런타임과 같은 자리·같은 식:
/// <c>StageZoom_Root.localScale = 1 + 0.05·zoom</c>(<c>PresentationCameraRootApplier</c>), <c>StagePan_Root.anchoredPosition = pan</c>,
/// 그리고 깊이 층마다 <c>FramingTransform</c>(pan 따라가기)·<c>FramingScale</c>(zoom 지수)의 패럴랙스(<c>PresentationResponseMath</c>).
/// 샷이 기본(zoom 0·pan 0)이면 전부 항등이다.
/// </summary>
public static class StageShotResponse
{
    public static void Apply(RectNodeTree tree, CameraSettings camera)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(camera);

        float scale = ShotIntentMath.EvaluateCameraScale(camera.Zoom);
        var pan = new Vec2(camera.PanX, camera.PanY);

        string zoomKey = StageNodes.KeyOf(StageNodes.ZoomRoot);
        string panKey = StageNodes.KeyOf(StageNodes.PanRoot);

        if (tree.Contains(zoomKey))
        {
            RectNodeState state = tree.GetState(zoomKey);
            tree.SetState(zoomKey, state.WithLocalScale(new Vec3(scale, scale, state.LocalScale.Z)));
        }

        if (tree.Contains(panKey))
        {
            tree.SetState(panKey, tree.GetState(panKey).WithAnchoredPosition(pan));
        }

        foreach (string stage in StageNodes.Stages)
        {
            foreach (string layer in SceneLayers.Order)
            {
                (Vec2 panResponse, float zoomResponse) = StageNodes.ResponseOf(layer);
                string transformKey = StageNodes.KeyOf(StageNodes.FramingTransformOf(stage, layer));
                string scaleKey = StageNodes.KeyOf(StageNodes.FramingScaleOf(stage, layer));

                if (tree.Contains(transformKey))
                {
                    // destPos = base(0) + focus 산개(0 — focus 점·층 루트가 모두 원점) + pan ⊙ panResponse
                    tree.SetState(transformKey, tree.GetState(transformKey).WithAnchoredPosition(new Vec2(pan.X * panResponse.X, pan.Y * panResponse.Y)));
                }

                if (tree.Contains(scaleKey))
                {
                    // destScale = base(1) × scale ^ zoomScaleResponse
                    float multiplier = MathF.Pow(scale, zoomResponse);
                    RectNodeState state = tree.GetState(scaleKey);
                    tree.SetState(scaleKey, state.WithLocalScale(new Vec3(multiplier, multiplier, state.LocalScale.Z)));
                }
            }
        }
    }
}
