using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Graph;

/// <summary>
/// 마야 타임 슬라이더의 키 조작 — 현재 시각의 키 전부를 한 덩어리로 다룬다(복사·지우기·인비트윈).
/// 순수 산수. 편집은 <c>MotionEditor</c>가 한다.
/// </summary>
public static class TimeSliderOps
{
    /// <summary>모든 커브에서 그 시각(허용 오차 안)의 키.</summary>
    public static List<KeyRef> KeysAt(MotionAsset motion, double seconds)
    {
        ArgumentNullException.ThrowIfNull(motion);
        var result = new List<KeyRef>();

        foreach (ChannelAsset channel in motion.Channels)
        {
            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is not { } curve)
                {
                    continue;
                }

                for (int i = 0; i < curve.Keys.Count; i++)
                {
                    if (Math.Abs(curve.Keys[i].T - seconds) <= MotionRules.Tolerance)
                    {
                        result.Add(new KeyRef(new CurveRef(channel.Node, channel.Kind, axis), i));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>모든 커브에서 [a, b] 구간(허용 오차 포함)의 키 — 타임 슬라이더의 빨간 범위 선택.</summary>
    public static List<KeyRef> KeysBetween(MotionAsset motion, double a, double b)
    {
        ArgumentNullException.ThrowIfNull(motion);
        double lo = Math.Min(a, b) - MotionRules.Tolerance;
        double hi = Math.Max(a, b) + MotionRules.Tolerance;
        var result = new List<KeyRef>();

        foreach (ChannelAsset channel in motion.Channels)
        {
            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is not { } curve)
                {
                    continue;
                }

                for (int i = 0; i < curve.Keys.Count; i++)
                {
                    if (curve.Keys[i].T >= lo && curve.Keys[i].T <= hi)
                    {
                        result.Add(new KeyRef(new CurveRef(channel.Node, channel.Kind, axis), i));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 인비트윈 — <paramref name="atSeconds"/> 뒤의 키를 전부 <paramref name="deltaSeconds"/>만큼 민다
    /// (마야 Add Inbetween = +1프레임, Remove Inbetween = −1프레임). 그 시각의 키는 안 움직인다.
    /// 뺄 때 <paramref name="atSeconds"/> 아래로는 안 내려가고, 닿은 자리의 키는 움직인 키가 이긴다.
    /// 돌려주는 것은 바뀐 커브만 — 그대로 <c>SetKeysMany</c>에 준다.
    /// </summary>
    public static List<(CurveRef Curve, IEnumerable<KeyAsset> Keys)> Inbetween(MotionAsset motion, float atSeconds, float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(motion);
        var edits = new List<(CurveRef, IEnumerable<KeyAsset>)>();

        if (Math.Abs(deltaSeconds) <= MotionRules.Tolerance)
        {
            return edits;
        }

        foreach (ChannelAsset channel in motion.Channels)
        {
            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is not { } curve)
                {
                    continue;
                }

                var kept = new List<KeyAsset>();
                var moved = new List<KeyAsset>();

                foreach (KeyAsset key in curve.Keys)
                {
                    if (key.T > atSeconds + MotionRules.Tolerance)
                    {
                        KeyAsset copy = Clone(key);
                        copy.T = Math.Max(atSeconds, key.T + deltaSeconds);
                        moved.Add(copy);
                    }
                    else
                    {
                        kept.Add(Clone(key));
                    }
                }

                if (moved.Count == 0)
                {
                    continue;
                }

                // 움직인 키가 이긴다 — 뒤에 오는(원래 더 늦은) 키가 같은 자리의 앞 키를 덮는다.
                foreach (KeyAsset key in moved)
                {
                    kept.RemoveAll(k => Math.Abs(k.T - key.T) <= MotionRules.Tolerance);
                    kept.Add(key);
                }

                edits.Add((new CurveRef(channel.Node, channel.Kind, axis), kept));
            }
        }

        return edits;
    }

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
