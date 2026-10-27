using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Graph;

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
