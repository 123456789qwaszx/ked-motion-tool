using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Graph;

/// <summary>마야 Pre/Post Infinity — 첫/마지막 키 밖을 어떻게 이어 보이나. 보기 전용(산출물의 끝은 restore/commit).</summary>
public enum InfinityMode
{
    /// <summary>끝값 그대로(런타임과 같다).</summary>
    Constant,

    /// <summary>키 구간을 되풀이.</summary>
    Cycle,

    /// <summary>되풀이하되 한 바퀴마다 끝값 차이만큼 올린다(걷기).</summary>
    CycleOffset,

    /// <summary>앞뒤로 왕복.</summary>
    Oscillate,

    /// <summary>끝 키의 기울기로 직선.</summary>
    Linear,
}

/// <summary>
/// 그릴 폴리라인. 코어 <see cref="CurveFunctions.Evaluate"/> 그대로라 에디터가 보여 주는 모양 = 미리보기가 재생하는 모양 =
/// 런타임이 재생하는 모양이다. 키 범위 밖은 끝값으로 평평하다(클램프 — 런타임과 같다).
/// </summary>
public static class CurveSampler
{
    /// <summary>[t0, t1]을 count 구간으로 — 키 시각은 반드시 샘플에 포함시켜 모서리가 뭉개지지 않게 한다.</summary>
    public static List<GraphPoint> Sample(IReadOnlyList<KeyAsset> keys, double t0, double t1, int count)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var points = new List<GraphPoint>();

        if (keys.Count == 0 || count < 1 || t1 <= t0)
        {
            return points;
        }

        CurveKey[] core = TangentMath.ToCurveKeys(keys);
        var times = new SortedSet<double>();

        for (int i = 0; i <= count; i++)
        {
            times.Add(t0 + (t1 - t0) * i / count);
        }

        foreach (KeyAsset key in keys)
        {
            if (key.T >= t0 && key.T <= t1)
            {
                times.Add(key.T);
            }
        }

        foreach (double t in times)
        {
            points.Add(new GraphPoint(t, CurveFunctions.Evaluate(core, (float)t)));
        }

        return points;
    }

    /// <summary>
    /// 길이로 감아 반복한 곡선(사이클 미리보기 — 마야 pre/post infinity cycle). 값은 <c>t mod duration</c>의 값이다.
    /// 산출물에는 없는 보기다 — 실제 끝은 restore/commit 규칙이 정한다.
    /// </summary>
    public static List<GraphPoint> SampleCycled(IReadOnlyList<KeyAsset> keys, double duration, double t0, double t1, int count)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var points = new List<GraphPoint>();

        if (keys.Count == 0 || count < 1 || t1 <= t0 || duration <= 0)
        {
            return points;
        }

        CurveKey[] core = TangentMath.ToCurveKeys(keys);

        for (int i = 0; i <= count; i++)
        {
            double t = t0 + (t1 - t0) * i / count;
            double wrapped = ((t % duration) + duration) % duration;
            points.Add(new GraphPoint(t, CurveFunctions.Evaluate(core, (float)wrapped)));
        }

        return points;
    }

    /// <summary>
    /// 첫 키~마지막 키 밖을 pre/post 무한 모드로 이은 곡선(마야 Infinity 보기). 키가 둘보다 적거나 구간이 0이면 빈 목록.
    /// 구간 안은 보통 평가와 같다.
    /// </summary>
    public static List<GraphPoint> SampleInfinity(IReadOnlyList<KeyAsset> keys, InfinityMode pre, InfinityMode post, double t0, double t1, int count)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var points = new List<GraphPoint>();

        if (keys.Count < 2 || count < 1 || t1 <= t0)
        {
            return points;
        }

        double a = keys[0].T;
        double b = keys[^1].T;
        double span = b - a;

        if (span <= 1e-9)
        {
            return points;
        }

        CurveKey[] core = TangentMath.ToCurveKeys(keys);
        float va = keys[0].V;
        float vb = keys[^1].V;

        for (int i = 0; i <= count; i++)
        {
            double t = t0 + (t1 - t0) * i / count;
            double v;

            if (t >= a && t <= b)
            {
                v = CurveFunctions.Evaluate(core, (float)t);
            }
            else
            {
                InfinityMode mode = t < a ? pre : post;
                v = mode switch
                {
                    InfinityMode.Cycle => CurveFunctions.Evaluate(core, (float)(a + Wrap(t - a, span))),
                    InfinityMode.CycleOffset => CurveFunctions.Evaluate(core, (float)(a + Wrap(t - a, span))) + Math.Floor((t - a) / span) * (vb - va),
                    InfinityMode.Oscillate => CurveFunctions.Evaluate(core, (float)(a + Reflect(t - a, span))),
                    InfinityMode.Linear => t < a ? va + keys[0].InTangent * (t - a) : vb + keys[^1].OutTangent * (t - b),
                    _ => t < a ? va : vb,
                };
            }

            points.Add(new GraphPoint(t, v));
        }

        return points;
    }

    private static double Wrap(double x, double m) => ((x % m) + m) % m;

    private static double Reflect(double x, double span)
    {
        double u = Wrap(x, 2 * span);
        return u > span ? 2 * span - u : u;
    }

    /// <summary>키들의 값 범위. 키가 없으면 false.</summary>
    public static bool TryBounds(IEnumerable<KeyAsset> keys, out double tMin, out double tMax, out double vMin, out double vMax)
    {
        tMin = double.PositiveInfinity;
        tMax = double.NegativeInfinity;
        vMin = double.PositiveInfinity;
        vMax = double.NegativeInfinity;
        bool any = false;

        foreach (KeyAsset key in keys)
        {
            any = true;
            tMin = Math.Min(tMin, key.T);
            tMax = Math.Max(tMax, key.T);
            vMin = Math.Min(vMin, key.V);
            vMax = Math.Max(vMax, key.V);
        }

        return any;
    }
}
