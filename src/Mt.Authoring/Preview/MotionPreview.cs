using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Preview;

/// <summary>
/// 시각 t의 포즈 — 리그 트리(덤프에서 세운 시작 상태)에 모션 채널을 얹은 것.
///
/// 수학은 전부 코어다: 시작값 <see cref="MotionMath.StartOf"/>, 합성 <see cref="MotionMath.Compose"/>,
/// 종점 <see cref="MotionMath.EndValue"/>. 그래서 뷰포트가 보여 주는 포즈 = 런타임이 재생하는 포즈다.
///
/// 끝 규칙도 런타임과 같다: 길이를 지나면 restore는 시작 상태(남기는 것이 없다), commit은 마지막 키(클레임)다.
/// relative/absolute는 모션의 <see cref="MotionAsset.PreviewMode"/>(뷰포트의 렌즈)다 — 재생에서는 motion 커맨드의 인자가 그 자리다.
/// </summary>
public static class MotionPreview
{
    /// <param name="isMuted">뮤트된 성분(재생에서 뺀다 — 시작값 그대로). null이면 없음.</param>
    /// <param name="keyPrefix">슬롯 접두사(예: "c1/") — 씬에서 오브젝트 하나에 얹을 때. 비면 접두사 없는 트리.</param>
    public static RectNodeTree Pose(RectNodeTree template, MotionAsset motion, double seconds, Func<Graph.CurveRef, bool>? isMuted = null, string keyPrefix = "")
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(motion);
        keyPrefix ??= "";

        RectNodeTree tree = template.Clone();
        MotionClip clip = MotionAssetCodec.ToClip(motion);

        MotionChannel[] channels = clip.Channels ?? Array.Empty<MotionChannel>();

        if (isMuted is not null)
        {
            for (int i = 0; i < channels.Length; i++)
            {
                MotionChannel ch = channels[i];

                if (isMuted(new Graph.CurveRef(ch.Node, ch.Kind, Axis.X)))
                {
                    ch.X = null;
                }

                if (isMuted(new Graph.CurveRef(ch.Node, ch.Kind, Axis.Y)))
                {
                    ch.Y = null;
                }

                if (isMuted(new Graph.CurveRef(ch.Node, ch.Kind, Axis.Z)))
                {
                    ch.Z = null;
                }

                channels[i] = ch;
            }
        }
        bool ended = seconds >= motion.Duration - 1e-6;

        if (ended && clip.End == MotionEnd.Restore)
        {
            return tree;
        }

        float t = (float)Math.Max(0, seconds);

        foreach (MotionChannel channel in channels)
        {
            // 덤프에 없는 노드는 건너뛴다 — 검증이 이미 소리를 낸다. 여기서 던지면 편집 중 화면이 죽는다.
            string key = keyPrefix + channel.Node;

            if (string.IsNullOrEmpty(channel.Node) || !tree.Contains(key))
            {
                continue;
            }

            if (!channel.HasX && !channel.HasY && !channel.HasZ)
            {
                continue;
            }

            RectNodeState state = tree.GetState(key);
            Vec3 start = MotionMath.StartOf(channel, state);
            Vec3 value = ended ? MotionMath.EndValue(channel, motion.PreviewMode, start) : MotionMath.Compose(channel, motion.PreviewMode, start, t);
            tree.SetState(key, Apply(channel.Kind, state, value));
        }

        return tree;
    }

    /// <summary>채널 종류가 노드 상태의 어느 자리를 쓰는가 — 코어 <c>StageNodeClaim</c>과 같은 대응.</summary>
    public static RectNodeState Apply(MotionChannelKind kind, in RectNodeState state, Vec3 value) => kind switch
    {
        MotionChannelKind.Position => state.WithAnchoredPosition(value.XY),
        MotionChannelKind.Scale => state.WithLocalScale(new Vec3(value.X, value.Y, state.LocalScale.Z)),
        MotionChannelKind.Euler => state.WithLocalEuler(value),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "모르는 채널 종류"),
    };
}
