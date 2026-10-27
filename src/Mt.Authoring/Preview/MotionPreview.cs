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
/// 내보내기와 같은 변환(<see cref="MotionAssetCodec.ToClip"/>)을 지나므로 relative 전 구간 중립값 성분은 여기서도 빠진다.
/// </summary>
public static class MotionPreview
{
    /// <param name="isMuted">뮤트된 성분(재생에서 뺀다 — 시작값 그대로). null이면 없음.</param>
    public static RectNodeTree Pose(RectNodeTree template, MotionAsset motion, double seconds, Func<Graph.CurveRef, bool>? isMuted = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(motion);

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
            if (string.IsNullOrEmpty(channel.Node) || !tree.Contains(channel.Node))
            {
                continue;
            }

            if (!channel.HasX && !channel.HasY && !channel.HasZ)
            {
                continue;
            }

            RectNodeState state = tree.GetState(channel.Node);
            Vec3 start = MotionMath.StartOf(channel, state);
            Vec3 value = ended ? MotionMath.EndValue(channel, start) : MotionMath.Compose(channel, start, t);
            tree.SetState(channel.Node, Apply(channel.Kind, state, value));
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
