using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Graph;

/// <summary>모션의 키 시각 목록 — 타임 슬라이더의 틱과 다음/이전 키 점프(`.` `,`)에 쓴다.</summary>
public static class KeyTimes
{
    /// <summary>모든 성분의 키 시각을 허용 오차로 합쳐 오름차순으로.</summary>
    public static List<float> All(MotionAsset motion)
    {
        ArgumentNullException.ThrowIfNull(motion);
        var times = new List<float>();

        foreach (ChannelAsset channel in motion.Channels)
        {
            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is not { } curve)
                {
                    continue;
                }

                foreach (KeyAsset key in curve.Keys)
                {
                    if (!times.Any(t => Math.Abs(t - key.T) <= MotionRules.Tolerance))
                    {
                        times.Add(key.T);
                    }
                }
            }
        }

        times.Sort();
        return times;
    }

    /// <summary>선택된 키들의 시각(허용 오차로 합침).</summary>
    public static List<float> OfSelection(MotionAsset motion, IEnumerable<KeyRef> selection)
    {
        ArgumentNullException.ThrowIfNull(motion);
        var times = new List<float>();

        foreach (KeyRef key in selection)
        {
            if (key.Resolve(motion) is { } k && !times.Any(t => Math.Abs(t - k.T) <= MotionRules.Tolerance))
            {
                times.Add(k.T);
            }
        }

        times.Sort();
        return times;
    }

    /// <summary>now보다 뒤의 첫 키 시각. 없으면 null.</summary>
    public static float? Next(IReadOnlyList<float> times, double now)
    {
        foreach (float t in times)
        {
            if (t > now + MotionRules.Tolerance)
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>now보다 앞의 마지막 키 시각. 없으면 null.</summary>
    public static float? Previous(IReadOnlyList<float> times, double now)
    {
        for (int i = times.Count - 1; i >= 0; i--)
        {
            if (times[i] < now - MotionRules.Tolerance)
            {
                return times[i];
            }
        }

        return null;
    }
}
