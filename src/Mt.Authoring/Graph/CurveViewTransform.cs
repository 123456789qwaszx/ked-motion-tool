using Mt.Authoring.Model;

namespace Mt.Authoring.Graph;

public enum GraphViewMode
{
    /// <summary>값 그대로.</summary>
    Absolute,

    /// <summary>커브마다 제 최소~최대를 0~1로 눌러 모양만 견준다(마야 Normalize).</summary>
    Normalized,

    /// <summary>정규화한 커브를 위아래로 쌓는다 — i번째 커브는 i~i+1(마야 Stacked).</summary>
    Stacked,
}

/// <summary>
/// 커브 하나의 값 ↔ 보기 값. 보기 값 = 값 × Scale + Offset. 탄젠트(기울기)는 Scale만 곱한다.
/// 평평한 커브(범위 0)는 1:1로 두고 가운데(0.5)에 놓는다 — 0으로 나누지 않고, 끌면 값이 그대로 바뀐다.
/// </summary>
public readonly record struct CurveViewTransform(double Scale, double Offset)
{
    public static CurveViewTransform Identity => new(1, 0);

    public double ToView(double value) => value * Scale + Offset;

    public double ToValue(double view) => (view - Offset) / Scale;

    public static CurveViewTransform For(IReadOnlyList<KeyAsset> keys, GraphViewMode mode, int stackIndex)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (mode == GraphViewMode.Absolute)
        {
            return Identity;
        }

        double band = mode == GraphViewMode.Stacked ? stackIndex : 0;

        if (!CurveSampler.TryBounds(keys, out _, out _, out double min, out double max) || max - min < 1e-6)
        {
            double center = keys.Count == 0 ? 0 : min;
            return new CurveViewTransform(1, band + 0.5 - center);
        }

        double scale = 1.0 / (max - min);
        return new CurveViewTransform(scale, band - min * scale);
    }
}
